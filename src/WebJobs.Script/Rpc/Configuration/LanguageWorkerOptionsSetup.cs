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

        public LanguageWorkerOptionsSetup(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            _configuration = configuration;
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryLanguageWorkerConfig);
        }

        public void Configure(LanguageWorkerOptions options)
        {
            var configFactory = new WorkerConfigFactory(_configuration, _logger);
            options.WorkerConfigs = configFactory.GetConfigs();
        }
    }
}
