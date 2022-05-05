// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal class WorkerProfileManager : IWorkerProfileManager
    {
        private readonly ILogger _logger;
        private readonly IEnumerable<IWorkerProfileConditionProvider> _conditionProviders;
        private Dictionary<string, List<WorkerDescriptionProfile>> _profiles = new Dictionary<string, List<WorkerDescriptionProfile>>();
        private string _activeProfile;

        public WorkerProfileManager(ILogger logger,
                                             IEnumerable<IWorkerProfileConditionProvider> conditionProviders)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _conditionProviders = conditionProviders ?? throw new ArgumentNullException(nameof(conditionProviders));
            _activeProfile = string.Empty;
        }

        public void SaveWorkerDescriptionProfiles(List<WorkerDescriptionProfile> workerDescriptionProfiles, string language)
        {
            _profiles.Add(language, workerDescriptionProfiles);
        }

        private bool GetEvaluatedProfile(string language, out WorkerDescriptionProfile evaluatedProfile)
        {
            if (_profiles.TryGetValue(language, out List<WorkerDescriptionProfile> profiles))
            {
                foreach (var profile in profiles)
                {
                    if (profile.EvaluateConditions())
                    {
                        evaluatedProfile = profile;
                        return true;
                    }
                }
            }
            evaluatedProfile = null;
            return false;
        }

        public void LoadWorkerDescriptionFromProfiles(RpcWorkerDescription defaultWorkerDescription, out RpcWorkerDescription workerDescription)
        {
            if (GetEvaluatedProfile(defaultWorkerDescription.Language, out WorkerDescriptionProfile profile))
            {
                _logger?.LogInformation($"Worker initialized with profile - {profile.Name}, Profile ID {profile.ProfileId} from worker config.");
                _activeProfile = profile.ProfileId;
                workerDescription = profile.ApplyProfile(defaultWorkerDescription);
            }
            workerDescription = defaultWorkerDescription;
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

        public bool IsCorrectProfileLoaded(string workerRuntime)
        {
            var profileId = string.Empty;
            if (GetEvaluatedProfile(workerRuntime, out WorkerDescriptionProfile profile))
            {
               profileId = profile.ProfileId;
            }
            return _activeProfile.Equals(profileId);
        }
    }
}
