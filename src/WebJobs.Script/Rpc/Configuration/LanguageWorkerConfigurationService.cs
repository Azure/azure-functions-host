// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class LanguageWorkerConfigurationService : ILanguageWorkerConfigurationService
    {
        private readonly ILogger _logger;

        private IList<WorkerConfig> _workerConfigs;
        private IConfiguration _configuration;

        public LanguageWorkerConfigurationService(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            _configuration = configuration;
            _logger = loggerFactory.CreateLogger<LanguageWorkerConfigurationService>();
        }

        public IList<WorkerConfig> WorkerConfigs
        {
            get
            {
                if (_workerConfigs != null)
                {
                    return _workerConfigs;
                }
                return GetLanguageWorkerConfigs();
            }
        }

        public void Reload(IConfiguration configuration)
        {
            _workerConfigs = null;
            _configuration = configuration;
        }

        private IList<WorkerConfig> GetLanguageWorkerConfigs()
        {
            var configFactory = new WorkerConfigFactory(_configuration, _logger);
            _workerConfigs = configFactory.GetConfigs();
            return _workerConfigs;
        }
    }
}
