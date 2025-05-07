// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly ILogger<FunctionAppValidationService> _logger;
        private readonly IOptions<ScriptJobHostOptions> _scriptOptions;

        public FunctionAppValidationService(
            ILogger<FunctionAppValidationService> logger,
            IOptions<ScriptJobHostOptions> scriptOptions,
            IEnvironment environment)
        {
            _scriptOptions = scriptOptions ?? throw new ArgumentNullException(nameof(scriptOptions));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            // Adding a delay to ensure that this validation does not impact the cold start performance
            Utility.ExecuteAfterColdStartDelay(_environment, Validate, cancellationToken);

            await Task.CompletedTask;
        }

        internal void Validate()
        {
            if (!_scriptOptions.Value.IsDefaultHostConfig &&
                Utility.IsDotnetIsolatedApp(environment: _environment) &&
                !Directory.Exists(Path.Combine(_scriptOptions.Value.RootScriptPath, ScriptConstants.AzureFunctionsSystemDirectoryName)))
            {
                _logger.MissingAzureFunctionsFolder();
            }
        }
    }
}