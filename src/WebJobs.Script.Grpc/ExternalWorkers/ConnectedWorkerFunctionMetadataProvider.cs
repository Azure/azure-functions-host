// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// An <see cref="IWorkerFunctionMetadataProvider"/> implementation that retrieves function metadata
/// from an already-connected external worker channel rather than spawning a new worker process.
/// This provider waits for a channel to become available via <see cref="IConnectedWorkerChannelManager"/>
/// and then calls <see cref="IRpcWorkerChannel.GetFunctionMetadata()"/> over gRPC.
/// </summary>
internal class ConnectedWorkerFunctionMetadataProvider : IWorkerFunctionMetadataProvider
{
    private const string MetadataProviderName = "ConnectedWorker";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    private readonly IConnectedWorkerChannelManager _channelManager;
    private readonly ILogger<ConnectedWorkerFunctionMetadataProvider> _logger;
    private readonly IWorkerRuntimeResolver _workerRuntimeResolver;
    private readonly Dictionary<string, ICollection<string>> _functionErrors = new();

    private ImmutableArray<FunctionMetadata> _functions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectedWorkerFunctionMetadataProvider"/> class.
    /// </summary>
    /// <param name="channelManager">The manager used to wait for connected worker channels.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="workerRuntimeResolver">The resolver used to determine the current worker runtime.</param>
    public ConnectedWorkerFunctionMetadataProvider(
        IConnectedWorkerChannelManager channelManager,
        ILogger<ConnectedWorkerFunctionMetadataProvider> logger,
        IWorkerRuntimeResolver workerRuntimeResolver)
    {
        _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _workerRuntimeResolver = workerRuntimeResolver ?? throw new ArgumentNullException(nameof(workerRuntimeResolver));
    }

    /// <inheritdoc />
    public ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors
        => _functionErrors.ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray());

    /// <inheritdoc />
    public async Task<FunctionMetadataResult> GetFunctionMetadataAsync(IEnumerable<RpcWorkerConfig> workerConfigs, bool forceRefresh = false)
    {
        if (!_functions.IsDefaultOrEmpty && !forceRefresh)
        {
            return new FunctionMetadataResult(useDefaultMetadataIndexing: false, _functions);
        }

        _logger.ReadingFunctionMetadataFromProvider(MetadataProviderName);

        _logger.LogInformation("Waiting for an external worker channel to connect.");
        IRpcWorkerChannel channel = await _channelManager.WaitForChannelAsync(DefaultTimeout, CancellationToken.None);

        _logger.LogInformation("External worker channel connected. Fetching function metadata.");
        List<RawFunctionMetadata> rawFunctions = await channel.GetFunctionMetadata();

        if (rawFunctions is not null && rawFunctions.Any(x => x.UseDefaultMetadataIndexing))
        {
            _functions = ImmutableArray<FunctionMetadata>.Empty;
            return new FunctionMetadataResult(useDefaultMetadataIndexing: true, _functions);
        }

        IEnumerable<FunctionMetadata> functions;
        if (rawFunctions is null || rawFunctions.Count == 0)
        {
            functions = Enumerable.Empty<FunctionMetadata>();
        }
        else
        {
            functions = ValidateMetadata(rawFunctions);
        }

        _functions = functions.ToImmutableArray();
        _logger.FunctionsReturnedByProvider(_functions.Length, MetadataProviderName);

        return new FunctionMetadataResult(useDefaultMetadataIndexing: false, _functions);
    }

    internal IEnumerable<FunctionMetadata> ValidateMetadata(IEnumerable<RawFunctionMetadata> functions)
    {
        List<FunctionMetadata> validatedMetadata = new();
        if (functions is null || !functions.Any())
        {
            _logger.LogDebug("There is no metadata to be validated.");
            return validatedMetadata;
        }

        _functionErrors.Clear();
        foreach (RawFunctionMetadata rawFunction in functions)
        {
            FunctionMetadata function = rawFunction.Metadata;
            try
            {
                Utility.ValidateName(function.Name);

                function.Language = _workerRuntimeResolver.GetWorkerRuntime();

                // Configuration source validation
                if (!string.IsNullOrEmpty(rawFunction.ConfigurationSource))
                {
                    JToken isDirect = JToken.Parse(rawFunction.ConfigurationSource);
                    var isDirectValue = isDirect?.ToString();
                    if (string.Equals(isDirectValue, "attributes", StringComparison.OrdinalIgnoreCase))
                    {
                        function.SetIsDirect(true);
                    }
                    else if (!string.Equals(isDirectValue, "config", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new FormatException($"Illegal value '{isDirectValue}' for 'configurationSource' property in {function.Name}'.");
                    }
                }

                // Populate retry options if a JSON string representation is provided
                if (!string.IsNullOrEmpty(rawFunction.RetryOptions))
                {
                    function.Retry = JObject.Parse(rawFunction.RetryOptions).ToObject<RetryOptions>();
                }

                if (function.Retry is not null)
                {
                    Utility.ValidateRetryOptions(function.Retry);
                }

                function = ValidateBindings(rawFunction.Bindings, function);

                validatedMetadata.Add(function);
            }
            catch (Exception ex)
            {
                Utility.AddFunctionError(_functionErrors, function.Name, Utility.FlattenException(ex, includeSource: false), isFunctionShortName: true);
            }
        }

        return validatedMetadata;
    }

    internal static FunctionMetadata ValidateBindings(IEnumerable<string> rawBindings, FunctionMetadata function)
    {
        HashSet<string> bindingNames = new(StringComparer.OrdinalIgnoreCase);

        function.Bindings.Clear();

        foreach (string binding in rawBindings)
        {
            var sanitizedBinding = MetadataJsonHelper.CreateJObjectWithSanitizedPropertyValue(
                binding, ScriptConstants.SensitiveMetadataBindingPropertyNames, DateParseHandling.None);
            var functionBinding = BindingMetadata.Create(sanitizedBinding);

            Utility.ValidateBinding(functionBinding);

            if (bindingNames.Contains(functionBinding.Name))
            {
                throw new InvalidOperationException(
                    $"{nameof(ConnectedWorkerFunctionMetadataProvider)}: Multiple bindings with name '{functionBinding.Name}' discovered. Binding names must be unique.");
            }

            bindingNames.Add(functionBinding.Name);
            function.Bindings.Add(functionBinding);
        }

        if (function.Bindings is null || function.Bindings.Count == 0)
        {
            throw new FormatException("At least one binding must be declared.");
        }

        var triggerMetadata = function.InputBindings.FirstOrDefault(p => p.IsTrigger);
        if (triggerMetadata is null)
        {
            throw new InvalidOperationException("No trigger binding specified. A function must have a trigger input binding.");
        }

        return function;
    }
}
