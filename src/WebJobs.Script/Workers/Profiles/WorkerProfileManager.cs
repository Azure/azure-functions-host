// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    /// <summary>
    /// The default profile manager that manages profiles from language workers
    /// </summary>
    internal class WorkerProfileManager : IWorkerProfileManager
    {
        private readonly ILogger _logger;
        private readonly IEnumerable<IWorkerProfileConditionProvider> _conditionProviders;
        private static Dictionary<string, List<WorkerDescriptionProfile>> _profiles = new Dictionary<string, List<WorkerDescriptionProfile>>();
        private static string _activeProfile = string.Empty;

        public WorkerProfileManager(ILogger logger, IEnumerable<IWorkerProfileConditionProvider> conditionProviders)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _conditionProviders = conditionProviders ?? throw new ArgumentNullException(nameof(conditionProviders));
        }

        /// <inheritdoc />
        public void SetWorkerDescriptionProfiles(List<WorkerDescriptionProfile> workerDescriptionProfiles, string language)
        {
            _profiles[language] = workerDescriptionProfiles;
        }

        // Evaluate profile conditions for a language
        private bool GetEvaluatedProfile(string language, out WorkerDescriptionProfile evaluatedProfile)
        {
            if (_profiles.TryGetValue(language, out List<WorkerDescriptionProfile> profiles))
            {
                foreach (var profile in profiles)
                {
                    if (profile.EvaluateConditions())
                    {
                        _logger?.LogInformation($"Conditions evaluated succesfully for profile with name: {profile.Name} and ID: {profile.ProfileId}");
                        evaluatedProfile = profile;
                        return true;
                    }
                }
            }
            evaluatedProfile = null;
            return false;
        }

        /// <inheritdoc />
        public void LoadWorkerDescriptionFromProfiles(RpcWorkerDescription defaultWorkerDescription, out RpcWorkerDescription workerDescription)
        {
            if (GetEvaluatedProfile(defaultWorkerDescription.Language, out WorkerDescriptionProfile profile))
            {
                _logger?.LogInformation($"Worker initialized with profile - {profile.Name}, Profile ID {profile.ProfileId} from worker config.");
                _activeProfile = profile.ProfileId;
                workerDescription = profile.ApplyProfile(defaultWorkerDescription);
                return;
            }
            workerDescription = defaultWorkerDescription;
        }

        /// <inheritdoc />
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

        /// <inheritdoc />
        public bool IsCorrectProfileLoaded(string workerRuntime)
        {
            var profileId = string.Empty;
            if (GetEvaluatedProfile(workerRuntime, out WorkerDescriptionProfile profile))
            {
               profileId = profile.ProfileId;
            }
            _logger?.LogInformation($"Active profile: {_activeProfile} Evaluated profile: {profileId}");
            return _activeProfile.Equals(profileId);
        }
    }
}
