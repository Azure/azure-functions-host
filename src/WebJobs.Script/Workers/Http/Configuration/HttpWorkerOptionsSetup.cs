// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net;
using System.Net.Sockets;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Http
{
    internal class HttpWorkerOptionsSetup : IConfigureOptions<HttpWorkerOptions>
    {
        private IConfiguration _configuration;
        private ILogger _logger;
        private ScriptJobHostOptions _scriptJobHostOptions;

        public HttpWorkerOptionsSetup(IOptions<ScriptJobHostOptions> scriptJobHostOptions, IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            _scriptJobHostOptions = scriptJobHostOptions.Value;
            _configuration = configuration;
            _logger = loggerFactory.CreateLogger<HttpWorkerOptionsSetup>();
        }

        public void Configure(HttpWorkerOptions options)
        {
            IConfigurationSection jobHostSection = _configuration.GetSection(ConfigurationSectionNames.JobHost);
            var httpWorkerSection = jobHostSection.GetSection(ConfigurationSectionNames.HttpWorker);
            if (httpWorkerSection.Exists())
            {
                httpWorkerSection.Bind(options);
                HttpWorkerDescription httpWorkerDescription = options.Description;

                if (httpWorkerDescription == null)
                {
                    throw new HostConfigurationException($"Missing WorkerDescription for HttpWorker");
                }
                httpWorkerDescription.ApplyDefaultsAndValidate(_scriptJobHostOptions.RootScriptPath, _logger);
                options.Arguments = new WorkerProcessArguments()
                {
                    ExecutablePath = options.Description.DefaultExecutablePath,
                    WorkerPath = options.Description.DefaultWorkerPath
                };

                options.Arguments.ExecutableArguments.AddRange(options.Description.Arguments);
                options.Port = GetUnusedTcpPort();
                _logger.LogDebug("Configured httpWorker with {DefaultExecutablePath}: {exepath} with arguments {args}", nameof(options.Description.DefaultExecutablePath), options.Description.DefaultExecutablePath, options.Arguments);
            }
        }

        internal static int GetUnusedTcpPort()
        {
            using (Socket tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                tcpSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                int port = ((IPEndPoint)tcpSocket.LocalEndPoint).Port;
                return port;
            }
        }
    }
}