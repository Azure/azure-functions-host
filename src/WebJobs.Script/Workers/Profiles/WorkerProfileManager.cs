// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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
        private readonly IEnvironment _environment;
        private readonly IEnumerable<IWorkerProfileConditionProvider> _conditionProviders;

        private Dictionary<string, string> _activeProfiles;
        private Dictionary<string, List<WorkerDescriptionProfile>> _profiles;

        public WorkerProfileManager(ILogger<WorkerProfileManager> logger, IEnvironment environment)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _activeProfiles = new Dictionary<string, string>();
            _profiles = new Dictionary<string, List<WorkerDescriptionProfile>>();
            _conditionProviders = new List<IWorkerProfileConditionProvider>
            {
                new WorkerProfileConditionProvider(_logger, _environment)
            };
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
                _activeProfiles[defaultWorkerDescription.Language] = profile.ProfileId;
                workerDescription = profile.ApplyProfile(defaultWorkerDescription);
            }
            else
            {
                workerDescription = defaultWorkerDescription;
            }
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

            string activeProfile;
            if (!_activeProfiles.TryGetValue(workerRuntime, out activeProfile))
            {
                activeProfile = string.Empty;
            }
            return activeProfile.Equals(profileId);
        }
    }
}
