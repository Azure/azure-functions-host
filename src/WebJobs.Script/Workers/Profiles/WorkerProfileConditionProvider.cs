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
        private readonly IProcessFacts _processFacts;

        public WorkerProfileConditionProvider(
            ILogger logger, IEnvironment environment, IProcessFacts processFacts)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _processFacts = processFacts ?? throw new ArgumentNullException(nameof(processFacts));
        }

        /// <inheritdoc />
        public bool TryCreateCondition(WorkerProfileConditionDescriptor descriptor, out IWorkerProfileCondition condition)
        {
            condition = descriptor.Type switch
            {
                WorkerConstants.WorkerDescriptionProfileHostPropertyCondition => new HostPropertyCondition(_logger, _processFacts, descriptor),
                WorkerConstants.WorkerDescriptionProfileEnvironmentCondition => new EnvironmentCondition(_logger, _environment, descriptor),
                _ => null
            };

            return condition != null;
        }
    }
}
