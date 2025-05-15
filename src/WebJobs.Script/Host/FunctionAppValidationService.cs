// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    internal sealed class FunctionAppValidationService : BackgroundService
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
            if (!_scriptOptions.Value.IsStandbyConfiguration)
            {
                // Adding a delay to ensure that this validation does not impact the cold start performance
                Utility.ExecuteAfterColdStartDelay(_environment, Validate, cancellationToken);
            }

            await Task.CompletedTask;
        }

        private void Validate()
        {
            try
            {
                string azureFunctionsDirPath = Path.Combine(_scriptOptions.Value.RootScriptPath, ScriptConstants.AzureFunctionsSystemDirectoryName);

                if (_scriptOptions.Value.RootScriptPath is not null &&
                    !_scriptOptions.Value.IsDefaultHostConfig &&
                    Utility.IsDotnetIsolatedApp(environment: _environment) &&
                    !Directory.Exists(azureFunctionsDirPath))
                {
                    // Search for the .azurefunctions directory within nested directories to verify scenarios where it isn't located at the root. This situation occurs when a function app has been improperly zipped.
                    IEnumerable<string> azureFunctionsDirectories = Directory.GetDirectories(_scriptOptions.Value.RootScriptPath, ScriptConstants.AzureFunctionsSystemDirectoryName, SearchOption.AllDirectories)
                        .Where(dir => !dir.Equals(azureFunctionsDirPath, StringComparison.OrdinalIgnoreCase));

                    if (azureFunctionsDirectories.Any())
                    {
                        string azureFunctionsDirectoriesPath = string.Join(", ", azureFunctionsDirectories).Replace(_scriptOptions.Value.RootScriptPath, string.Empty);
                        _logger.IncorrectAzureFunctionsFolderPath(azureFunctionsDirectoriesPath);
                    }
                    else
                    {
                        _logger.MissingAzureFunctionsFolder();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace("Unable to validate deployed function app payload", ex);
            }
        }
    }
}