// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal sealed class WorkerProfileConditionProvider : IWorkerProfileConditionProvider
    {
        private readonly ILogger<WorkerProfileConditionProvider> _logger;
        private readonly IEnvironment _environment;
        private readonly ISystemRuntimeInformation _systemRuntimeInformation;

        public WorkerProfileConditionProvider(ILogger<WorkerProfileConditionProvider> logger, ISystemRuntimeInformation systemRuntimeInfo, IEnvironment environment)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _systemRuntimeInformation = systemRuntimeInfo ?? SystemRuntimeInformation.Instance;
        }

        /// <inheritdoc />
        public bool TryCreateCondition(WorkerProfileConditionDescriptor descriptor, out IWorkerProfileCondition condition)
        {
            condition = descriptor.Type switch
            {
                WorkerConstants.WorkerDescriptionProfileHostPropertyCondition => new HostPropertyCondition(_logger, _systemRuntimeInformation, descriptor),
                WorkerConstants.WorkerDescriptionProfileEnvironmentCondition => new EnvironmentCondition(_logger, _environment, descriptor),
                _ => null
            };

            return condition != null;
        }
    }
}
