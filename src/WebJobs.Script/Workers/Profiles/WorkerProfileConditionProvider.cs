// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Conditions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.Profiles
{
    internal sealed class WorkerProfileConditionProvider : IConditionProvider
    {
        private readonly ILogger _logger;
        private readonly IEnvironment _environment;

        public WorkerProfileConditionProvider(ILogger logger, IEnvironment environment)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        public bool TryCreateCondition(ConditionDescriptor descriptor, out ICondition condition)
        {
            if (descriptor == null)
            {
                condition = null;
                return false;
            }

            condition = descriptor.Type switch
            {
                ConditionConstants.HostPropertyConditionType => new HostPropertyCondition(_logger, SystemRuntimeInformation.Instance, descriptor),
                ConditionConstants.EnvironmentConditionType => new EnvironmentCondition(_logger, _environment, descriptor),
                _ => null
            };

            return condition != null;
        }
    }
}
