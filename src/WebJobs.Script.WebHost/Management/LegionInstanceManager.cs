// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class LegionInstanceManager : IInstanceManager
    {
        private static readonly object _assignmentLock = new object();
        private static HostAssignmentContext _assignmentContext;

        private readonly ILogger _logger;
        private readonly IMetricsLogger _metricsLogger;
        private readonly IMeshServiceClient _meshServiceClient;
        private readonly IEnvironment _environment;
        private readonly HttpClient _client;
        private readonly IScriptWebHostEnvironment _webHostEnvironment;

        public LegionInstanceManager(IHttpClientFactory httpClientFactory, IScriptWebHostEnvironment webHostEnvironment,
            IEnvironment environment, ILogger<LegionInstanceManager> logger, IMetricsLogger metricsLogger, IMeshServiceClient meshServiceClient)
        {
            _client = httpClientFactory?.CreateClient() ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _webHostEnvironment = webHostEnvironment ?? throw new ArgumentNullException(nameof(webHostEnvironment));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _metricsLogger = metricsLogger;
            _meshServiceClient = meshServiceClient;
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        public Task<string> SpecializeMSISidecar(HostAssignmentContext context)
        {
            // Skip since Legion will take care of MSI Specialization
            return null;
        }

        public bool StartAssignment(HostAssignmentContext context)
        {
            if (!_webHostEnvironment.InStandbyMode)
            {
                // This is only true when specializing pinned containers.
                if (!context.Environment.TryGetValue(EnvironmentSettingNames.ContainerStartContext, out string startContext))
                {
                    _logger.LogError("Assign called while host is not in placeholder mode and start context is not present.");
                    return false;
                }
            }

            if (_environment.IsContainerReady())
            {
                _logger.LogError("Assign called while container is marked as specialized.");
                return false;
            }

            if (context.IsWarmupRequest)
            {
                // No need to download since file already exists
                return true;
            }
            else if (_assignmentContext == null)
            {
                lock (_assignmentLock)
                {
                    if (_assignmentContext != null)
                    {
                        return _assignmentContext.Equals(context);
                    }
                    _assignmentContext = context;
                }

                _logger.LogInformation($"Starting Assignment. Cloud Name: {_environment.GetCloudName()}");

                // set a flag which will cause any incoming http requests to buffer
                // until specialization is complete
                // the host is guaranteed not to receive any requests until AFTER assign
                // has been initiated, so setting this flag here is sufficient to ensure
                // that any subsequent incoming requests while the assign is in progress
                // will be delayed until complete
                _webHostEnvironment.DelayRequests();

                // start the specialization process in the background
                Task.Run(async () => await Assign(context));

                return true;
            }
            else
            {
                // No lock needed here since _assignmentContext is not null when we are here
                return _assignmentContext.Equals(context);
            }
        }

        public Task<string> ValidateContext(HostAssignmentContext assignmentContext)
        {
            // Don't need to validate RunFromPackageContext in Legion
            return null;
        }

        private async Task Assign(HostAssignmentContext assignmentContext)
        {
            try
            {
                // first make all environment and file system changes required for
                // the host to be specialized
                _logger.LogInformation($"Applying {assignmentContext.Environment.Count} app setting(s)");
                assignmentContext.ApplyAppSettings(_environment, _logger);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Assign failed");
                await _meshServiceClient.NotifyHealthEvent(ContainerHealthEventType.Fatal, GetType(), "Assign failed");
                throw;
            }
            finally
            {
                // all assignment settings/files have been applied so we can flip
                // the switch now on specialization
                // even if there are failures applying context above, we want to
                // leave placeholder mode
                _logger.LogInformation("Triggering specialization");
                _webHostEnvironment.FlagAsSpecializedAndReady();

                _webHostEnvironment.ResumeRequests();
            }
        }

        public IDictionary<string, string> GetInstanceInfo()
        {
            return null;
        }

        // for testing
        internal static void Reset()
        {
            _assignmentContext = null;
        }
    }
}
