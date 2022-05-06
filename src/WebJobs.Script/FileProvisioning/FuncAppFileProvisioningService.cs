// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.FileProvisioning
{
    internal class FuncAppFileProvisioningService : IHostedService
    {
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _options;
        private readonly IEnvironment _environment;
        private readonly ILogger _logger;
        private readonly IFuncAppFileProvisionerFactory _funcAppFileProvisionerFactory;

        public FuncAppFileProvisioningService(
            IEnvironment environment,
            IOptionsMonitor<ScriptApplicationHostOptions> options,
            IFuncAppFileProvisionerFactory funcAppFileProvisionerFactory,
            ILogger logger)
        {
            _environment = environment;
            _options = options;
            _funcAppFileProvisionerFactory = funcAppFileProvisionerFactory;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (!_options.CurrentValue.IsFileSystemReadOnly)
                {
                    var funcAppFileProvisioner = _funcAppFileProvisionerFactory.CreatFileProvisioner(_environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName));
                    if (funcAppFileProvisioner != null)
                    {
                        await funcAppFileProvisioner.ProvisionFiles(_options.CurrentValue.ScriptPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting FuncAppFileProvisioning Service. Handling error and continuing.");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
