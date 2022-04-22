// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal class WorkerProfileConditionManager : IWorkerProfileConditionManager
    {
        private readonly ILogger _logger;
        private readonly IEnumerable<IWorkerProfileConditionProvider> _conditionProviders;

        public WorkerProfileConditionManager(ILogger logger,
                                             IEnumerable<IWorkerProfileConditionProvider> conditionProviders)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _conditionProviders = conditionProviders ?? throw new ArgumentNullException(nameof(conditionProviders));
        }

        public bool TryCreateWorkerProfileCondition(WorkerProfileConditionDescriptor conditionDescriptor, out IWorkerProfileCondition condition)
        {
            foreach (var provider in _conditionProviders)
            {
                if (provider.TryCreateCondition(conditionDescriptor, out condition))
                {
                    return true;
                }
            }

            _logger.LogInformation("Unable to create profile condition for condition type '{conditionType}'", conditionDescriptor.Type);

            condition = null;
            return false;
        }
    }
}
