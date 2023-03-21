// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.Profiles
{
    internal sealed class WorkerProfileConditionProvider : IWorkerProfileConditionProvider
    {
        private readonly ILogger _logger;
        private readonly IEnvironment _environment;

        public WorkerProfileConditionProvider(ILogger logger, IEnvironment environment)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        /// <inheritdoc />
        public bool TryCreateCondition(WorkerProfileConditionDescriptor descriptor, out IWorkerProfileCondition condition)
        {
            condition = descriptor.Type switch
            {
                WorkerConstants.WorkerDescriptionProfileHostPropertyCondition => new HostPropertyCondition(_logger, SystemRuntimeInformation.Instance, descriptor),
                WorkerConstants.WorkerDescriptionProfileEnvironmentCondition => new EnvironmentCondition(_logger, _environment, descriptor),
                _ => null
            };

            return condition != null;
        }
    }
}
