// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Host
{
    /// <summary>
    /// A background service responsible for validating function app payload.
    /// </summary>
    public sealed class FunctionAppValidationService : BackgroundService
    {
        private readonly IEnvironment _environment;
        private readonly ILogger _logger;
        private readonly IOptions<ScriptJobHostOptions> _scriptOptions;

        public FunctionAppValidationService(
            ILoggerFactory loggerFactory,
            IOptions<ScriptJobHostOptions> scriptOptions,
            IEnvironment environment)
        {
            _scriptOptions = scriptOptions ?? throw new ArgumentNullException(nameof(scriptOptions));
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
        }

        public void RunValidation(IOptions<ScriptJobHostOptions> scriptOptions, CancellationToken cancellationToken = default)
        {
            // Adding a delay to ensure that this validation does not impact the cold start performance
            Utility.ExecuteAfterColdStartDelay(_environment, () => Validate(scriptOptions), cancellationToken);
        }

        internal void Validate(IOptions<ScriptJobHostOptions> scriptOptions)
        {
            if (!_environment.IsPlaceholderModeEnabled() &&
                !scriptOptions.Value.IsStandbyConfiguration &&
                !scriptOptions.Value.IsDefaultHostConfig &&
                Utility.IsDotnetIsolatedApp(environment: _environment) &&
                scriptOptions.Value.RootScriptPath is not null &&
                !Directory.Exists(Path.Combine(scriptOptions.Value.RootScriptPath, ScriptConstants.AzureFunctionsSystemDirectoryName)))
            {
                _logger.MissingAzureFunctionsFolder();
            }
        }
    }
}