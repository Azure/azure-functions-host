// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
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

        public IWorkerProfileCondition CreateWorkerProfileCondition(string type, string name, string expression)
        {
            Enum.TryParse(type, out ConditionType conditionType);
            switch (conditionType)
            {
                case ConditionType.HostProperty:
                    return new HostPropertyCondition(_logger, _systemRuntimeInformation, _environment, name, expression);
                case ConditionType.Environment:
                    return new EnvironmentCondition(_logger, _systemRuntimeInformation, _environment, name, expression);
                default:
                    throw new ArgumentException(nameof(type));
            }
        }
    }
}
