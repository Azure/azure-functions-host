// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Host
{
    /// <summary>
    /// A background service responsible for validating function app payload.
    /// </summary>
    internal sealed class FunctionAppValidationService : BackgroundService
    {
        private readonly IEnvironment _environment;
        private readonly ILogger _logger;
        private readonly IOptions<ScriptJobHostOptions> _scriptOptions;
        private readonly IEnumerable<IFunctionAppValidator> _validators;

        public FunctionAppValidationService(
            ILoggerFactory loggerFactory,
            IOptions<ScriptJobHostOptions> scriptOptions,
            IEnvironment environment,
            IEnumerable<IFunctionAppValidator> validators)
        {
            _scriptOptions = scriptOptions ?? throw new ArgumentNullException(nameof(scriptOptions));
            _logger = loggerFactory?.CreateLogger(ScriptConstants.LogCategoryHostGeneral) ?? throw new ArgumentNullException(nameof(loggerFactory));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _validators = validators ?? throw new ArgumentNullException(nameof(validators));
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            if (!_scriptOptions.Value.IsStandbyConfiguration)
            {
                // Adding a delay to ensure that this validation does not impact the cold start performance
                Utility.ExecuteAfterColdStartDelay(_environment, Validate, cancellationToken);
            }
            await Task.CompletedTask;
        }

        private void Validate()
        {
            foreach (var validator in _validators)
            {
                try
                {
                    validator.Validate(_scriptOptions.Value, _environment, _logger);
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Validator {ValidatorType} failed", validator.GetType().Name);
                }
            }
        }
    }
}