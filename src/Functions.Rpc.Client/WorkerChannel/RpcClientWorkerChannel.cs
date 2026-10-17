// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Runs the shared worker protocol over a client-owned FunctionRpc channel.
/// </summary>
/// <remarks>
/// Concurrent and repeated starts share one initialization attempt. A token already canceled when <see cref="StartAsync"/> is called does not
/// begin initialization. Once initialization begins, cancellation only stops the individual caller's wait; shared initialization continues
/// until it succeeds, fails, times out, or the channel is disposed.
/// </remarks>
internal sealed class RpcClientWorkerChannel : WorkerChannel
{
    internal const string ManagedIdentityTokenHttpClientName = "ManagedIdentityTokenService";

    private const string CurrentClientIdQuery = "client_id";
    private const string LegacyClientIdQuery = "clientid";
    private const string LegacyIdentityHeaderName = "Secret";
    private const string LegacyMsiApiVersion = "2017-09-01";
    private const string LegacyMsiApiVersionAlias = "0.1";
    private const int MaximumManagedIdentityTokenResponseBytes = 1024 * 1024;
    private static readonly TimeSpan ManagedIdentityTokenRequestTimeout = TimeSpan.FromSeconds(8);
    private readonly CancellationTokenSource _managedIdentityTokenRequestCancellationSource = new();
    private readonly CancellationToken _managedIdentityTokenRequestCancellationToken;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Lock _lifecycleLock = new();
    private readonly ILogger _logger;
    private readonly string _runtimeAccessKey;
    private readonly TaskCompletionSource _startCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private LifecycleState _lifecycleState;

    public RpcClientWorkerChannel(
        string workerId,
        DuplexChannel<StreamingMessage> ownedChannel,
        IScriptEventManager eventManager,
        IScriptHostManager hostManager,
        RpcWorkerConfig workerConfig,
        ILogger logger,
        IMetricsLogger metricsLogger,
        int attemptCount,
        IEnvironment environment,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions,
        ISharedMemoryManager sharedMemoryManager,
        IOptions<WorkerConcurrencyOptions> workerConcurrencyOptions,
        IOptions<FunctionsHostingConfigOptions> hostingConfigOptions,
        IAppCapabilitiesStore appCapabilitiesStore,
        IHttpProxyService httpProxyService,
        string runtimeAccessKey)
        : base(
            workerId,
            ownedChannel,
            eventManager,
            hostManager,
            workerConfig,
            logger,
            metricsLogger,
            attemptCount,
            environment,
            applicationHostOptions,
            sharedMemoryManager,
            workerConcurrencyOptions,
            hostingConfigOptions,
            appCapabilitiesStore,
            httpProxyService)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _runtimeAccessKey = runtimeAccessKey;
        _managedIdentityTokenRequestCancellationToken = _managedIdentityTokenRequestCancellationSource.Token;
        Completion = ownedChannel.Reader.Completion;
    }

    private enum LifecycleState
    {
        NotStarted,
        Started,
        Disposed,
    }

    internal Task Completion { get; }

    /// <summary>
    /// Starts inbound protocol processing and waits for the worker initialization handshake.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels this caller's wait without canceling shared initialization.</param>
    /// <returns>A task that completes after a successful WorkerInitResponse, is canceled for this caller, or faults when initialization cannot complete.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_lifecycleState is LifecycleState.Disposed, this);

            if (_lifecycleState is LifecycleState.NotStarted)
            {
                _lifecycleState = LifecycleState.Started;

                try
                {
                    MarkWorkerInitializing();
                    BeginInboundProcessing(ValidateStartStreamTimeout(WorkerConfig.CountOptions.ProcessStartupTimeout));
                    _ = CompleteStartAsync();
                }
                catch (Exception exception)
                {
                    _startCompletion.TrySetException(exception);
                }
            }
        }

        return _startCompletion.Task.WaitAsync(cancellationToken);
    }

    protected override void OnMetadataRequestError(Exception exception) => FailPendingMetadataRequest(exception);

    protected override void OnChannelFailure(Exception exception) => FailPendingMetadataRequest(exception);

    protected override void HandleManagedIdentityTokenRequest(StreamingMessage message)
    {
        _ = HandleManagedIdentityTokenRequestAsync(message);
    }

    protected override void Dispose(bool disposing)
    {
        lock (_lifecycleLock)
        {
            _lifecycleState = LifecycleState.Disposed;
        }

        if (disposing)
        {
            _managedIdentityTokenRequestCancellationSource.Cancel();
            // Metadata callers did not request cancellation; fail so host startup can retry.
            FailPendingMetadataRequest(new ObjectDisposedException(GetType().FullName));
        }

        base.Dispose(disposing);

        if (disposing)
        {
            _managedIdentityTokenRequestCancellationSource.Dispose();
        }
    }

    private async Task CompleteStartAsync()
    {
        try
        {
            if (!await WorkerInitialization.ConfigureAwait(false))
            {
                throw new InvalidOperationException("The worker reported unsuccessful initialization.");
            }

            _startCompletion.TrySetResult();
        }
        catch (OperationCanceledException exception)
        {
            _startCompletion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            _startCompletion.TrySetException(exception);
        }
    }

    private async Task HandleManagedIdentityTokenRequestAsync(StreamingMessage message)
    {
        ManagedIdentityTokenResponse response;
        if (!ManagedIdentityTokenRequestAuthenticator.IsValid(
            _runtimeAccessKey,
            message.RequestId,
            message.ManagedIdentityTokenRequest))
        {
            _logger.LogWarning("The runtime rejected an unauthenticated managed identity token request.");
            response = CreateManagedIdentityFailure("The managed identity token request is not authenticated.");
        }
        else
        {
            try
            {
                response = await RequestManagedIdentityTokenAsync(
                    message.ManagedIdentityTokenRequest,
                    _managedIdentityTokenRequestCancellationToken);
            }
            catch (OperationCanceledException) when (_managedIdentityTokenRequestCancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException exception)
            {
                _logger.LogWarning(exception, "The runtime managed identity token request timed out.");
                response = CreateManagedIdentityFailure("The runtime managed identity token request timed out.");
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "The runtime managed identity token request failed.");
                response = CreateManagedIdentityFailure("The runtime managed identity token request failed.");
            }
        }

        if (_managedIdentityTokenRequestCancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await SendStreamingMessageAsync(new()
            {
                RequestId = message.RequestId,
                ManagedIdentityTokenResponse = response
            });
        }
        catch (Exception exception) when (_managedIdentityTokenRequestCancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(exception, "The managed identity token response was canceled during channel shutdown.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The managed identity token response could not be sent.");
        }
    }

    private async Task<ManagedIdentityTokenResponse> RequestManagedIdentityTokenAsync(
        ManagedIdentityTokenRequest request,
        CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        (Uri endpoint, string secret, string secretHeaderName) = CreateManagedIdentityTokenRequest(request);
        using HttpRequestMessage httpRequest = new(HttpMethod.Get, endpoint);
        httpRequest.Headers.Add(secretHeaderName, secret);
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(ManagedIdentityTokenRequestTimeout);
        HttpClient httpClient = _httpClientFactory.CreateClient(ManagedIdentityTokenHttpClientName);
        using HttpResponseMessage httpResponse = await httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            timeoutSource.Token);
        await httpResponse.Content.LoadIntoBufferAsync(
            MaximumManagedIdentityTokenResponseBytes,
            timeoutSource.Token);
        byte[] body = await httpResponse.Content.ReadAsByteArrayAsync(timeoutSource.Token);
        _logger.LogInformation(
            "The runtime managed identity token request completed with status code {StatusCode} after {ElapsedMilliseconds} ms.",
            (int)httpResponse.StatusCode,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        return new()
        {
            Result = new() { Status = StatusResult.Types.Status.Success },
            StatusCode = (int)httpResponse.StatusCode,
            ContentType = httpResponse.Content.Headers.ContentType?.ToString() ?? string.Empty,
            Body = ByteString.CopyFrom(body)
        };
    }

    private (Uri Endpoint, string Secret, string SecretHeaderName) CreateManagedIdentityTokenRequest(
        ManagedIdentityTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Resource) || string.IsNullOrWhiteSpace(request.ApiVersion))
        {
            throw new InvalidOperationException("The managed identity token request is invalid.");
        }

        string endpointValue = _configuration[EnvironmentSettingNames.MsiEndpoint];
        string secret = _configuration[EnvironmentSettingNames.MsiSecret];
        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out Uri endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || string.IsNullOrEmpty(secret))
        {
            throw new InvalidOperationException("The runtime managed identity endpoint is not configured.");
        }

        UriBuilder endpointBuilder = new(endpoint);
        List<string> query = [];
        if (!string.IsNullOrEmpty(endpointBuilder.Query))
        {
            query.Add(endpointBuilder.Query.TrimStart('?'));
        }

        query.Add($"resource={Uri.EscapeDataString(request.Resource)}");
        query.Add($"api-version={Uri.EscapeDataString(request.ApiVersion)}");
        bool usesLegacyContract = UsesLegacyManagedIdentityContract(request.ApiVersion);
        if (!string.IsNullOrWhiteSpace(request.ClientId))
        {
            string clientIdQuery = usesLegacyContract ? LegacyClientIdQuery : CurrentClientIdQuery;
            query.Add($"{clientIdQuery}={Uri.EscapeDataString(request.ClientId)}");
        }

        endpointBuilder.Query = string.Join('&', query);
        return (
            endpointBuilder.Uri,
            secret,
            usesLegacyContract ? LegacyIdentityHeaderName : ScriptConstants.XIdentityHeader);
    }

    private static bool UsesLegacyManagedIdentityContract(string apiVersion)
        => string.Equals(apiVersion, LegacyMsiApiVersion, StringComparison.Ordinal)
            || string.Equals(apiVersion, LegacyMsiApiVersionAlias, StringComparison.Ordinal);

    private static ManagedIdentityTokenResponse CreateManagedIdentityFailure(string message)
    {
        return new()
        {
            Result = new()
            {
                Status = StatusResult.Types.Status.Failure,
                Exception = new() { Message = message }
            }
        };
    }

    private static TimeSpan ValidateStartStreamTimeout(TimeSpan startStreamTimeout)
    {
        if (startStreamTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(startStreamTimeout), "The StartStream timeout must be greater than zero.");
        }

        return startStreamTimeout;
    }
}
