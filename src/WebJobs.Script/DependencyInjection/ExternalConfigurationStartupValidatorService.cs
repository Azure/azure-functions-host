// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.DependencyInjection
{
    internal class ExternalConfigurationStartupValidatorService : IHostedService
    {
        private readonly ExternalConfigurationStartupValidator _validator;
        private readonly IConfigurationRoot _originalConfig;
        private readonly IEnvironment _environment;
        private readonly ILogger<ExternalConfigurationStartupValidator> _logger;

        public ExternalConfigurationStartupValidatorService(ExternalConfigurationStartupValidator validator, IConfigurationRoot originalConfig, IEnvironment environment, ILogger<ExternalConfigurationStartupValidator> logger)
        {
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _originalConfig = originalConfig ?? throw new ArgumentNullException(nameof(originalConfig));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            IDictionary<string, IEnumerable<string>> invalidValues = _validator.Validate(_originalConfig);

            if (invalidValues.Any())
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("The Functions scale controller may not scale the following functions correctly because some configuration values were modified in an external startup class.");

                foreach (KeyValuePair<string, IEnumerable<string>> invalidValueMap in invalidValues)
                {
                    sb.AppendLine($"  Function '{invalidValueMap.Key}' uses the modified key(s): {string.Join(", ", invalidValueMap.Value)}");
                }

                if (_environment.IsCoreTools())
                {
                    // We don't know where this will be deployed, so it may not matter,
                    // but log this as a warning during development.
                    _logger.LogWarning(sb.ToString());
                }
                else
                {
                    throw new HostInitializationException(sb.ToString());
                }
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
