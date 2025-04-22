using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using System.IO;

namespace Microsoft.Azure.WebJobs.Script
{
    public class FunctionMetadataValidationService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<FunctionMetadataValidationService> _logger;

        public FunctionMetadataValidationService(IServiceProvider serviceProvider, ILogger<FunctionMetadataValidationService> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var functionMetadataManager = scope.ServiceProvider.GetService<IFunctionMetadataManager>();
                if (functionMetadataManager == null)
                {
                    _logger.LogError("IFunctionMetadataManager is not registered in the service provider.");
                    throw new InvalidOperationException("FunctionMetadataManager is required for validation.");
                }

                /*
                    if (isDotnetIsolatedApp &&
                        !SystemEnvironment.Instance.IsPlaceholderModeEnabled() &&
                        !Directory.Exists(Path.Combine(_rootScriptPath, ScriptConstants.AzureFunctionsSystemDirectoryName)))
                    {
                        _logger.NoAzureFunctionsFolder();
                    }
                */

                // Retrieve the function metadata
                var functionMetadataList = functionMetadataManager.GetFunctionMetadata(forceRefresh: true);

                if (Directory.Exists(Path.Combine("C:\\FunctionsRepos\\Repros\\long-overdue-int-erubtion\\Functions.IntBug\\bin\\Debug\\net8.0", ScriptConstants.AzureFunctionsSystemDirectoryName)))
                {
                    _logger.NoAzureFunctionsFolder();
                }

                // Check if the list is empty
                if (functionMetadataList.IsDefaultOrEmpty)
                {
                    _logger.LogError("FunctionMetadataList is empty. Validation failed.");
                    throw new InvalidOperationException("FunctionMetadataList is empty. Ensure at least one valid function is configured.");
                }

                _logger.LogInformation("FunctionMetadataList validation succeeded. Functions are properly configured.");
            }

            await Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // No cleanup required for this service
            return Task.CompletedTask;
        }
    }
}