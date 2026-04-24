// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Conditions
{
    /// <summary>
    /// Condition provider used by the extension bundle loader.
    /// Always succeeds (returns true) — unknown condition types yield a
    /// <see cref="FalseCondition"/> so the caller iterates uniformly and
    /// a misconfigured bundle simply fails its requirements instead of crashing the host.
    /// </summary>
    public sealed class BundleConditionProvider : IConditionProvider
    {
        private readonly ILogger _logger;
        private readonly IEnvironment _environment;
        private readonly ISystemRuntimeInformation _systemRuntimeInformation;

        public BundleConditionProvider(ILogger logger, IEnvironment environment, ISystemRuntimeInformation systemRuntimeInformation)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _systemRuntimeInformation = systemRuntimeInformation ?? throw new ArgumentNullException(nameof(systemRuntimeInformation));
        }

        public bool TryCreateCondition(ConditionDescriptor descriptor, out ICondition condition)
        {
            if (descriptor == null)
            {
                _logger.LogWarning("Bundle requirement descriptor is null; substituting FalseCondition.");
                condition = new FalseCondition();
                return true;
            }

            condition = descriptor.Type switch
            {
                ConditionConstants.HostPropertyConditionType => new HostPropertyCondition(_logger, _systemRuntimeInformation, descriptor),
                ConditionConstants.EnvironmentConditionType => new EnvironmentCondition(_logger, _environment, descriptor),
                _ => Unknown(descriptor.Type)
            };

            return true;
        }

        private ICondition Unknown(string conditionType)
        {
            _logger.LogWarning("Unknown bundle requirement conditionType '{type}'; substituting FalseCondition.", conditionType);
            return new FalseCondition();
        }
    }
}
