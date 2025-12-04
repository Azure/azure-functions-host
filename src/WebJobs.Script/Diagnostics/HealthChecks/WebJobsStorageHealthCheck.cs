// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks
{
    /// <summary>
    /// Health check for the AzureWebJobsStorage used by WebJobs.
    /// </summary>
    /// <param name="provider">The blob storage provider.</param>
    /// <remarks>
    /// Checking connectivity to Azure Storage can be expensive and time consuming - especially
    /// waiting for a connection timeout in the event of no connectivity. To speed up health checks
    /// this class will periodically refresh the health check in the background. On
    /// <see cref="CheckHealthAsync(HealthCheckContext, CancellationToken)"/>, this class will wait for
    /// the first result. After that it will keep returning the most recent result of the background
    /// refresh.
    /// </remarks>
    internal class WebJobsStorageHealthCheck : IHealthCheck, IAsyncDisposable
    {
        private const string ConfigSection = "AzureWebJobsStorage";

        // Refresh in the background every 30 seconds by default.
        private static readonly TimeSpan DefaultPeriod = TimeSpan.FromSeconds(30);
        private readonly CancellationTokenSource _cts = new();
        private readonly Lazy<Task> _initialized;
        private readonly IEnvironment _environment;
        private readonly IAzureBlobStorageProvider _provider;

        private HealthCheckResult _last;
        private HealthCheckContext _context;
        private BlobServiceClient _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="WebJobsStorageHealthCheck"/> class.
        /// </summary>
        /// <param name="provider">The blob storage provider.</param>
        /// <param name="environment">The environment.</param>
        public WebJobsStorageHealthCheck(
            IAzureBlobStorageProvider provider, IEnvironment environment)
        {
            ArgumentNullException.ThrowIfNull(provider);
            ArgumentNullException.ThrowIfNull(environment);
            _provider = provider;
            _environment = environment;
            _initialized = new(RunInBackgroundAsync);
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelNoThrowAsync().ConfigureAwait(false);
            _cts.Dispose();
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);

            // TODO: we don't have access to StandbyOptions in this assembly.
            // Switch to using that when/if it ever moves here. It isn't worth moving to the other assembly
            // just to get access to that type, since technically IEnvironment is sufficient here.
            // TODO: a better approach would be to dynamically register only when specialization completes.
            // However, that is not supported with the current health check infrastructure -- it captures
            // the set of health checks at startup, and does not allow dynamic changes. We can revisit that later.
            if (_environment.IsPlaceholderModeEnabled())
            {
                // This health check runs in the WebHost context, as misconfigured AzureWebJobsStorage may prevent
                // the ScriptHost from starting. So we want to ensure this check runs independent of ScriptHost
                // starting. But we also want to avoid false negatives during placeholder mode.
                return HealthCheckResult.Healthy("Placeholder mode. Check skipped.");
            }

            _context = context;
            await _initialized.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            return _last;
        }

        private async Task<HealthCheckResult> CheckHealthCoreAsync(CancellationToken cancellation)
        {
            if (!TryGetClient(out BlobServiceClient client, out HealthCheckResult result))
            {
                return result;
            }

            try
            {
                // Right now we only check if we can access blobs. The functions host doesn't use queues or tables directly (although extensions may).
                // We use this API to check connectivity to Blob storage. We don't check permissions/role assignments in depth for a couple reasons:
                // 1. It is expensive
                // 2. Permissions/role assignments needed by the host can change over time.
                // So we settle for just connectivity here. Insufficient permissions will show up as errors elsewhere.
                // See https://docs.microsoft.com/en-us/azure/storage/common/storage-auth-aad-app?tabs=dotnet#configure-permissions-for-access-to-blob-and-queue-data
                await client
                    .GetBlobContainersAsync(cancellationToken: cancellation)
                    .AsPages(pageSizeHint: 1)
                    .GetAsyncEnumerator(cancellation)
                    .MoveNextAsync()
                    .ConfigureAwait(false);

                return HealthCheckResult.Healthy();
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                HealthCheckData data = GetData(ex, "connectivity");
                return HealthCheckResult.Unhealthy($"Unable to access {ConfigSection}", ex, data);
            }
        }

        private async Task RunInBackgroundAsync()
        {
            // Ensure we have at least one result to return right away.
            CancellationToken cancellation = _cts.Token;
            _last = await CheckHealthCoreAsync(cancellation).ConfigureAwait(false);

            // Kick off background refresh.
            Task.Run(
                async () =>
                {
                    while (!cancellation.IsCancellationRequested)
                    {
                        TimeSpan delay = _context?.Registration?.Period ?? DefaultPeriod;
                        await Task.Delay(delay, cancellation);
                        _last = await CheckHealthCoreAsync(cancellation).ConfigureAwait(false);
                    }
                })
                .Forget();
        }

        private static BlobServiceClient GetClient(IAzureBlobStorageProvider provider)
        {
            if (provider is HostAzureBlobStorageProvider hostProvider)
            {
                // Avoid TryCreate* to capture the original exception on failures.
                return hostProvider.CreateBlobServiceClient(ConnectionStringNames.Storage);
            }

            if (provider.TryCreateBlobServiceClientFromConnection(ConnectionStringNames.Storage, out BlobServiceClient blobServiceClient))
            {
                return blobServiceClient;
            }

            // TODO: need a better exception type.
            throw new InvalidOperationException("Failed to create BlobServiceClient.");
        }

        private bool TryGetClient(out BlobServiceClient client, out HealthCheckResult result)
        {
            try
            {
                // Only cache the client on success. On failure, we want to re-fetch the client next time
                // in case configuration has changed. This is to be defensive in-case config is still updating
                // from specialization.
                _client ??= GetClient(_provider);
                client = _client;
                result = HealthCheckResult.Healthy();
                return true;
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                client = null;
                HealthCheckData data = GetData(ex, "configuration");
                result = HealthCheckResult.Unhealthy($"Unable to create client for {ConfigSection}", ex, data);
                return false;
            }
        }

        private static HealthCheckData GetData(Exception ex, string area)
        {
            HealthCheckData data = new()
            {
                Area = area,
                ConfigurationSection = ConfigSection,
            };

            data.SetExceptionDetails(ex);
            return data;
        }
    }
}
