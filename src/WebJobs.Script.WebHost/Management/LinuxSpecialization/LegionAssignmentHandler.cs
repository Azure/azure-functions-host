// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization
{
    public class LegionAssignmentHandler : IAssignmentHandler
    {
        private readonly ILogger<LegionAssignmentHandler> _logger;

        public LegionAssignmentHandler(ILogger<LegionAssignmentHandler> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<string> ValidateContext(HostAssignmentContext assignmentContext)
        {
            _logger.LogInformation($"Skipping host assignment context (SiteId: {assignmentContext.SiteId}, SiteName: '{assignmentContext.SiteName}'. IsWarmup: '{assignmentContext.IsWarmupRequest}')");
            // WEBSITE_RUN_FROM_PACKAGE, SCM_RUN_FROM_PACKAGE & AzureFiles have been validated already.
            return Task.FromResult((string)null);
        }

        public Task<string> SpecializeMSISidecar(HostAssignmentContext context)
        {
            // Specializing sidecar is not the responsibility of Host
            return Task.FromResult((string)null);
        }

        public Task<string> Download(HostAssignmentContext context)
        {
            _logger.LogInformation($"Skipping download");
            return Task.FromResult((string)null);
        }

        public Task ApplyFileSystemChanges(HostAssignmentContext assignmentContext)
        {
            return Task.CompletedTask;
        }
    }
}