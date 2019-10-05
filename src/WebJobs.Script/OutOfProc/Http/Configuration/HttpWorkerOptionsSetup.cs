// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.OutOfProc.Http
{
    internal class HttpWorkerOptionsSetup : IConfigureOptions<HttpWorkerOptions>
    {
        private IConfiguration _configuration;
        private ILogger _logger;

        public HttpWorkerOptionsSetup(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            _configuration = configuration;
            _logger = loggerFactory.CreateLogger<HttpWorkerOptionsSetup>();
        }

        public void Configure(HttpWorkerOptions options)
        {
            IConfigurationSection jobHostSection = _configuration.GetSection(ConfigurationSectionNames.JobHost);
            var httpInvokerSection = jobHostSection.GetSection(ConfigurationSectionNames.HttpInvoker);
            if (httpInvokerSection.Exists())
            {
                httpInvokerSection.Bind(options);
                HttpWorkerDescription httpInvokerDescription = options.Description;

                if (httpInvokerDescription == null)
                {
                    throw new HostConfigurationException($"Missing WorkerDescription for HttpInvoker");
                }
                httpInvokerDescription.ApplyDefaultsAndValidate();
                if (string.IsNullOrEmpty(httpInvokerDescription.DefaultWorkerPath))
                {
                    if (!Path.IsPathRooted(httpInvokerDescription.DefaultExecutablePath))
                    {
                        httpInvokerDescription.DefaultExecutablePath = Path.Combine(httpInvokerDescription.WorkerDirectory, httpInvokerDescription.DefaultExecutablePath);
                    }
                }
                var arguments = new WorkerProcessArguments()
                {
                    ExecutablePath = options.Description.DefaultExecutablePath,
                    WorkerPath = options.Description.DefaultWorkerPath
                };

                arguments.ExecutableArguments.AddRange(options.Description.Arguments);
                _logger.LogDebug("Configured httpInvoker with {DefaultExecutablePath}: {exepath} with arguments {args}", nameof(options.Description.DefaultExecutablePath), options.Description.DefaultExecutablePath, options.Arguments);
            }
        }
    }
}
