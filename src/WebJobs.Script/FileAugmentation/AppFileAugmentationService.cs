// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.FileAugmentation
{
    internal class AppFileAugmentationService : IHostedService
    {
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _options;
        private readonly IEnvironment _environment;
        private readonly IFuncAppFileAugmentorFactory _funcAppFileAugmentorFactory;

        public AppFileAugmentationService(
            IEnvironment environment,
            IOptionsMonitor<ScriptApplicationHostOptions> options,
            IFuncAppFileAugmentorFactory funcAppFileAugmentorFactory)
        {
            _environment = environment;
            _options = options;
            _funcAppFileAugmentorFactory = funcAppFileAugmentorFactory;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_environment.FileSystemIsReadOnly())
            {
                var funcAppFileAugmentor = _funcAppFileAugmentorFactory.CreatFileAugmentor(_environment.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName));
                if (funcAppFileAugmentor != null)
                {
                    await funcAppFileAugmentor.AugmentFiles(_options.CurrentValue.ScriptPath);
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
