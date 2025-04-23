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

namespace Microsoft.Azure.WebJobs.Script
{
    public class FunctionAppValidationService : IHostedService
    {
        private readonly ILogger<FunctionAppValidationService> _logger;
        private IOptions<ScriptJobHostOptions> _scriptOptions;

        public FunctionAppValidationService(
            ILogger<FunctionAppValidationService> logger,
            IOptions<ScriptJobHostOptions> scriptOptions)
        {
            _scriptOptions = scriptOptions;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {

            if (Utility.IsDotnetIsolatedApp(null, SystemEnvironment.Instance) &&
                !SystemEnvironment.Instance.IsPlaceholderModeEnabled() &&
                !Directory.Exists(Path.Combine(_scriptOptions.Value.RootScriptPath, ScriptConstants.AzureFunctionsSystemDirectoryName)) &&
                !_scriptOptions.Value.IsDefaultHostConfig)
            {
                _logger.NoAzureFunctionsFolder();
            }

            await Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}