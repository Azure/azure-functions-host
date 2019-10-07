// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class LanguageWorkerOptionsSetup : IConfigureOptions<LanguageWorkerOptions>
    {
        private IConfiguration _configuration;
        private ILogger _logger;
        private IEnvironment _environment;

        public LanguageWorkerOptionsSetup(IConfiguration configuration, ILoggerFactory loggerFactory, IEnvironment environment)
        {
            _configuration = configuration;
            _logger = loggerFactory.CreateLogger("Host.LanguageWorkerConfig");
            _environment = environment;
        }

        public void Configure(LanguageWorkerOptions options)
        {
            ISystemRuntimeInformation systemRuntimeInfo = new SystemRuntimeInformation();
            var configFactory = new WorkerConfigFactory(_configuration, _logger, systemRuntimeInfo, _environment);
            options.WorkerConfigs = configFactory.GetConfigs();
        }
    }
}
