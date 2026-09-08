// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Grpc;

/// <summary>
/// Converts and validates raw metadata returned by a language worker.
/// </summary>
public sealed partial class WorkerFunctionMetadataValidator
{
    private readonly Lock _errorsLock = new();
    private readonly ILogger _logger;
    private readonly IWorkerRuntimeResolver _workerRuntimeResolver;
    private ImmutableDictionary<string, ImmutableArray<string>> _functionErrors =
        ImmutableDictionary<string, ImmutableArray<string>>.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerFunctionMetadataValidator"/> class.
    /// </summary>
    /// <param name="logger">The logger used for metadata diagnostics.</param>
    /// <param name="workerRuntimeResolver">The current worker runtime resolver.</param>
    public WorkerFunctionMetadataValidator(ILogger logger, IWorkerRuntimeResolver workerRuntimeResolver)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _workerRuntimeResolver = workerRuntimeResolver ?? throw new ArgumentNullException(nameof(workerRuntimeResolver));
    }

    /// <summary>
    /// Gets validation errors keyed by function name.
    /// </summary>
    public ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors
    {
        get
        {
            lock (_errorsLock)
            {
                return _functionErrors;
            }
        }
    }

    /// <summary>
    /// Validates raw worker metadata while retaining valid functions when another function is invalid.
    /// </summary>
    /// <param name="functions">The raw worker metadata.</param>
    /// <returns>The valid function metadata.</returns>
    public ImmutableArray<FunctionMetadata> ValidateMetadata(IEnumerable<RawFunctionMetadata> functions)
    {
        Dictionary<string, ICollection<string>> functionErrors = [];
        if (functions is null || !functions.Any())
        {
            Log.NoMetadata(_logger);
            SetFunctionErrors(ImmutableDictionary<string, ImmutableArray<string>>.Empty);
            return [];
        }

        ImmutableArray<FunctionMetadata>.Builder validatedMetadata = ImmutableArray.CreateBuilder<FunctionMetadata>();
        foreach (RawFunctionMetadata rawFunction in functions)
        {
            FunctionMetadata function = CloneFunctionMetadata(rawFunction?.Metadata);
            try
            {
                if (rawFunction is null)
                {
                    throw new FormatException("The worker returned an empty function metadata entry.");
                }

                if (function is null)
                {
                    throw new FormatException("The worker returned function metadata without a function definition.");
                }

                Utility.ValidateName(function.Name);
                function.Language = _workerRuntimeResolver.GetWorkerRuntime();

                if (!string.IsNullOrEmpty(rawFunction.ConfigurationSource))
                {
                    JToken configurationSource = JToken.Parse(rawFunction.ConfigurationSource);
                    string configurationSourceValue = configurationSource?.ToString();
                    if (string.Equals(configurationSourceValue, "attributes", StringComparison.OrdinalIgnoreCase))
                    {
                        function.SetIsDirect(true);
                    }
                    else if (!string.Equals(configurationSourceValue, "config", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new FormatException(
                            $"Illegal value '{configurationSourceValue}' for 'configurationSource' property in {function.Name}'.");
                    }
                }

                if (!string.IsNullOrEmpty(rawFunction.RetryOptions))
                {
                    function.Retry = JObject.Parse(rawFunction.RetryOptions).ToObject<RetryOptions>();
                }

                if (function.Retry is not null)
                {
                    Utility.ValidateRetryOptions(function.Retry);
                }

                validatedMetadata.Add(ValidateBindings(rawFunction.Bindings, function));
            }
            catch (Exception exception)
            {
                Utility.AddFunctionError(functionErrors, function?.Name, Utility.FlattenException(exception, includeSource: false),
                    isFunctionShortName: true);
            }
        }

        SetFunctionErrors(functionErrors.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.ToImmutableArray()));
        return validatedMetadata.ToImmutable();
    }

    internal static FunctionMetadata ValidateBindings(IEnumerable<string> rawBindings, FunctionMetadata function)
    {
        HashSet<string> bindingNames = new(StringComparer.OrdinalIgnoreCase);
        function.Bindings.Clear();

        foreach (string binding in rawBindings)
        {
            JObject sanitizedBinding = MetadataJsonHelper.CreateJObjectWithSanitizedPropertyValue(
                binding, ScriptConstants.SensitiveMetadataBindingPropertyNames, DateParseHandling.None);
            BindingMetadata functionBinding = BindingMetadata.Create(sanitizedBinding);

            Utility.ValidateBinding(functionBinding);
            if (!bindingNames.Add(functionBinding.Name))
            {
                throw new InvalidOperationException(
                    $"{nameof(WorkerFunctionDescriptorProvider)}: Multiple bindings with name '{functionBinding.Name}' discovered. Binding names must be unique.");
            }

            function.Bindings.Add(functionBinding);
        }

        if (function.Bindings is null || function.Bindings.Count == 0)
        {
            throw new FormatException("At least one binding must be declared.");
        }

        BindingMetadata triggerMetadata = function.InputBindings.FirstOrDefault(binding => binding.IsTrigger);
        if (triggerMetadata is null)
        {
            throw new InvalidOperationException("No trigger binding specified. A function must have a trigger input binding.");
        }

        return function;
    }

    private static FunctionMetadata CloneFunctionMetadata(FunctionMetadata source)
    {
        if (source is null)
        {
            return null;
        }

        FunctionMetadata clone = new()
        {
            EntryPoint = source.EntryPoint,
            FunctionDirectory = source.FunctionDirectory,
            Language = source.Language,
            Name = source.Name,
            Retry = source.Retry,
            ScriptFile = source.ScriptFile,
        };
        foreach (KeyValuePair<string, object> property in source.Properties)
        {
            clone.Properties.Add(property);
        }

        return clone;
    }

    private void SetFunctionErrors(ImmutableDictionary<string, ImmutableArray<string>> functionErrors)
    {
        lock (_errorsLock)
        {
            _functionErrors = functionErrors;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Debug, "There is no metadata to be validated.")]
        public static partial void NoMetadata(ILogger logger);
    }
}
