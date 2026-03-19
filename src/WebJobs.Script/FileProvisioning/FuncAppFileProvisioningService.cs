// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.FileProvisioning
{
    internal class FuncAppFileProvisioningService : IHostedService
    {
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _options;
        private readonly IWorkerRuntimeResolver _workerRuntimeResolver;
        private readonly IFuncAppFileProvisionerFactory _funcAppFileProvisionerFactory;

        public FuncAppFileProvisioningService(
            IWorkerRuntimeResolver workerRuntimeResolver,
            IOptionsMonitor<ScriptApplicationHostOptions> options,
            IFuncAppFileProvisionerFactory funcAppFileProvisionerFactory)
        {
            _options = options;
            _workerRuntimeResolver = workerRuntimeResolver ?? throw new ArgumentNullException(nameof(workerRuntimeResolver));
            _funcAppFileProvisionerFactory = funcAppFileProvisionerFactory;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_options.CurrentValue.IsFileSystemReadOnly)
            {
                var funcAppFileProvisioner = _funcAppFileProvisionerFactory.CreatFileProvisioner(_workerRuntimeResolver.GetWorkerRuntime());

                if (funcAppFileProvisioner != null)
                {
                    await funcAppFileProvisioner.ProvisionFiles(_options.CurrentValue.ScriptPath);
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
