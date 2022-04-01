// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal class WorkerProfileConditionFactory : IWorkerProfileConditionFactory
    {
        private readonly ILogger _logger;
        private readonly ISystemRuntimeInformation _systemRuntimeInformation;
        private readonly IEnvironment _environment;

        public WorkerProfileConditionFactory(ILogger logger, ISystemRuntimeInformation systemRuntimeInfo, IEnvironment environment)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _systemRuntimeInformation = systemRuntimeInfo ?? throw new ArgumentNullException(nameof(systemRuntimeInfo));
        }

        public IWorkerProfileCondition CreateWorkerProfileCondition(IDictionary<string, object> properties)
        {
            switch (properties[WorkerConstants.WorkerDescriptionProfileConditionType])
            {
                case WorkerConstants.WorkerDescriptionProfileHostPropertyCondition:
                    return new HostPropertyCondition(_logger, _systemRuntimeInformation, (string)properties["name"], (string)properties["expression"]);
                case WorkerConstants.WorkerDescriptionProfileEnvironmentCondition:
                    return new EnvironmentCondition(_logger, _environment, (string)properties["name"], (string)properties["expression"]);
                default:
                    throw new ArgumentException(nameof(WorkerConstants.WorkerDescriptionProfileConditionType));
            }
        }
    }
}
