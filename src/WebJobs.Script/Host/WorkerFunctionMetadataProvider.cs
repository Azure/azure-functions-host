﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class WorkerFunctionMetadataProvider : IWorkerFunctionMetadataProvider
    {
        private readonly Dictionary<string, ICollection<string>> _functionErrors = new Dictionary<string, ICollection<string>>();
        private readonly IOptions<ScriptApplicationHostOptions> _scriptApplicationHostOptions;
        private readonly ILogger _logger;
        private readonly IEnvironment _environment;
        private readonly IWebHostRpcWorkerChannelManager _channelManager;
        private string _workerRuntime;
        private ImmutableArray<FunctionMetadata> _functions;

        public WorkerFunctionMetadataProvider(
            IOptions<ScriptApplicationHostOptions> scriptOptions,
            ILogger<WorkerFunctionMetadataProvider> logger,
            IEnvironment environment,
            IWebHostRpcWorkerChannelManager webHostRpcWorkerChannelManager)
        {
            _scriptApplicationHostOptions = scriptOptions;
            _logger = logger;
            _environment = environment;
            _channelManager = webHostRpcWorkerChannelManager;
            _workerRuntime = _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime);
        }

        public ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors
           => _functionErrors.ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray());

        public async Task<FunctionMetadataResult> GetFunctionMetadataAsync(IEnumerable<RpcWorkerConfig> workerConfigs, bool forceRefresh)
        {
            _workerRuntime = _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime);

            _logger.LogInformation("Fetching metadata for workerRuntime: {workerRuntime}", _workerRuntime);

            IEnumerable<FunctionMetadata> functions = new List<FunctionMetadata>();
            _logger.FunctionMetadataProviderParsingFunctions();

            if (_functions.IsDefaultOrEmpty || forceRefresh)
            {
                IEnumerable<RawFunctionMetadata> rawFunctions = new List<RawFunctionMetadata>();

                if (_channelManager == null)
                {
                    throw new InvalidOperationException(nameof(_channelManager));
                }

                var channels = _channelManager.GetChannels(_workerRuntime);

                if (channels?.Any() != true)
                {
                    await _channelManager.InitializeChannelAsync(_workerRuntime);
                    channels = _channelManager.GetChannels(_workerRuntime);
                }
                // start up GRPC channels

                foreach (string workerId in channels.Keys.ToList())
                {
                    if (channels.TryGetValue(workerId, out TaskCompletionSource<IRpcWorkerChannel> languageWorkerChannelTask))
                    {
                        _logger.LogDebug("Found initialized language worker channel for runtime: {workerRuntime} workerId:{workerId}", _workerRuntime, workerId);
                        try
                        {
                            IRpcWorkerChannel channel = await languageWorkerChannelTask.Task;
                            rawFunctions = await channel.GetFunctionMetadata();

                            if (rawFunctions.Any(x => x.UseDefaultMetadataIndexing))
                            {
                                _functions.Clear();
                                return new FunctionMetadataResult(useDefaultMetadataIndexing: true, _functions);
                            }

                            if (!IsNullOrEmpty(rawFunctions))
                            {
                                functions = ValidateMetadata(rawFunctions);
                            }

                            _functions = functions.ToImmutableArray();
                            _logger.FunctionMetadataProviderFunctionFound(_functions.IsDefault ? 0 : _functions.Count());

                            // Validate if the app has functions in legacy format and add in logs to inform about the mixed app
                            _ = Task.Delay(TimeSpan.FromMinutes(1)).ContinueWith(t => ValidateFunctionAppFormat(_scriptApplicationHostOptions.Value.ScriptPath, _logger, _environment));

                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Removing errored webhost language worker channel for runtime: {workerRuntime} workerId:{workerId}", _workerRuntime, workerId);
                            await _channelManager.ShutdownChannelIfExistsAsync(_workerRuntime, workerId, ex);
                        }
                    }
                }
            }

            return new FunctionMetadataResult(useDefaultMetadataIndexing: false, _functions);
        }

        internal static void ValidateFunctionAppFormat(string scriptPath, ILogger logger, IEnvironment environment, IFileSystem fileSystem = null)
        {
            fileSystem = fileSystem ?? FileUtility.Instance;
            bool mixedApp = false;
            string legacyFormatFunctions = null;

            if (fileSystem.Directory.Exists(scriptPath))
            {
                var functionDirectories = fileSystem.Directory.EnumerateDirectories(scriptPath).ToImmutableArray();
                foreach (var functionDirectory in functionDirectories)
                {
                    if (Utility.TryReadFunctionConfig(functionDirectory, out string json, fileSystem))
                    {
                        mixedApp = true;
                        var functionName = functionDirectory.Split('\\').Last();
                        legacyFormatFunctions = legacyFormatFunctions != null ? legacyFormatFunctions + ", " + functionName : functionName;
                    }
                }

                if (mixedApp)
                {
                    string logMessage = $"Detected mixed function app. Some functions may not be indexed - {legacyFormatFunctions}";

                    if (environment.IsCoreTools())
                    {
                        logger.Log(LogLevel.Warning, logMessage + " Refer to the documentation to filter warning - https://docs.microsoft.com/en-us/azure/azure-functions/configure-monitoring?tabs=v2");
                    }
                    else
                    {
                        logger.Log(LogLevel.Information, logMessage);
                    }
                }
            }
        }

        internal IEnumerable<FunctionMetadata> ValidateMetadata(IEnumerable<RawFunctionMetadata> functions)
        {
            List<FunctionMetadata> validatedMetadata = new List<FunctionMetadata>();
            if (IsNullOrEmpty(functions))
            {
                _logger.LogDebug("There is no metadata to be validated.");
                return validatedMetadata;
            }
            _functionErrors.Clear();
            foreach (RawFunctionMetadata rawFunction in functions)
            {
                var function = rawFunction.Metadata;
                try
                {
                    Utility.ValidateName(function.Name);

                    function.Language = SystemEnvironment.Instance.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName);

                    // skip function directory validation because this involves reading function.json

                    // skip function ScriptFile validation for now because this involves enumerating file directory

                    // configuration source validation
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

                    // retry option validation
                    if (!string.IsNullOrEmpty(rawFunction.RetryOptions))
                    {
                        function.Retry = JObject.Parse(rawFunction.RetryOptions).ToObject<RetryOptions>();
                        Utility.ValidateRetryOptions(function.Retry);
                    }

                    // binding validation
                    function = ValidateBindings(rawFunction.Bindings, function);

                    // add validated metadata to validated list if it gets this far
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
            HashSet<string> bindingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string binding in rawBindings)
            {
                var functionBinding = BindingMetadata.Create(JObject.Parse(binding));

                Utility.ValidateBinding(functionBinding);

                // Ensure no duplicate binding names exist
                if (bindingNames.Contains(functionBinding.Name))
                {
                    throw new InvalidOperationException(string.Format("Multiple bindings with name '{0}' discovered. Binding names must be unique.", functionBinding.Name));
                }
                else
                {
                    bindingNames.Add(functionBinding.Name);
                }

                // add binding to function.Bindings once validation is complete
                function.Bindings.Add(functionBinding);
            }

            // ensure there is at least one binding after validation
            if (function.Bindings == null || function.Bindings.Count == 0)
            {
                throw new FormatException("At least one binding must be declared.");
            }

            // ensure that there is a trigger binding
            var triggerMetadata = function.InputBindings.FirstOrDefault(p => p.IsTrigger);
            if (triggerMetadata == null)
            {
                throw new InvalidOperationException("No trigger binding specified. A function must have a trigger input binding.");
            }

            return function;
        }

        private bool IsNullOrEmpty(IEnumerable<RawFunctionMetadata> functions)
        {
            if (functions == null || !functions.Any())
            {
                return true;
            }
            return false;
        }
    }
}
