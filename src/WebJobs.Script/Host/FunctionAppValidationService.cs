// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script
{
    public class FunctionAppValidationService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<FunctionAppValidationService> _logger;
        private IOptions<ScriptJobHostOptions> _scriptOptions;
        private readonly IEnvironment _environment;

        public FunctionAppValidationService(
            IServiceProvider serviceProvider,
            ILogger<FunctionAppValidationService> logger,
            IOptions<ScriptJobHostOptions> scriptOptions,
            IEnvironment environment)
        {
            _scriptOptions = scriptOptions;
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Adding a delay to ensure that this validation does not impact the cold start performance
            _ = Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ContinueWith(t => Validate());

            await Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        internal void Validate()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var functionMetadataManager = scope.ServiceProvider.GetService<IFunctionMetadataManager>();
                if (functionMetadataManager is not null)
                {
                    var functionMetadataList = functionMetadataManager.GetFunctionMetadata(forceRefresh: true);

                    if (Utility.IsDotnetIsolatedApp(functionMetadataList, _environment) &&
                    !SystemEnvironment.Instance.IsPlaceholderModeEnabled() &&
                    !Directory.Exists(Path.Combine(_scriptOptions.Value.RootScriptPath, ScriptConstants.AzureFunctionsSystemDirectoryName)) &&
                    !_scriptOptions.Value.IsDefaultHostConfig)
                    {
                        _logger.NoAzureFunctionsFolder();
                    }
                }
            }
        }
    }
}