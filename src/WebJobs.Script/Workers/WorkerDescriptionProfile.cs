// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public class WorkerDescriptionProfile
    {
        public WorkerDescriptionProfile(string name, List<IWorkerProfileCondition> conditions, RpcWorkerDescription profileDescription)
        {
            Name = name;
            Conditions = conditions;
            ProfileDescription = profileDescription;
            Validate();
        }

        /// <summary>
        /// Gets or sets the name of the profile.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the conditions which must be met for the profile to be used.
        /// </summary>
        public List<IWorkerProfileCondition> Conditions { get; set; }

        /// <summary>
        /// Gets or sets the worker description for the profile
        /// </summary>
        public RpcWorkerDescription ProfileDescription { get; set; }

        public void Validate()
        {
            if (string.IsNullOrEmpty(Name))
            {
                throw new ValidationException($"WorkerDescriptionProfile {nameof(Name)} cannot be empty");
            }

            if (Conditions == null || Conditions.Count < 1)
            {
                throw new ValidationException($"WorkerDescriptionProfile {nameof(Conditions)} cannot be empty");
            }

            if (ProfileDescription == null)
            {
                throw new ValidationException($"WorkerDescriptionProfile {nameof(ProfileDescription)} cannot be null");
            }
        }

        public bool EvaluateConditions()
        {
            foreach (var condition in Conditions)
            {
                if (!condition.Evaluate())
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Overrides the worker description parameters to that available in the profile
        /// </summary>
        public RpcWorkerDescription ApplyProfile(RpcWorkerDescription defaultWorkerDescription)
        {
            defaultWorkerDescription.Arguments = ProfileDescription.Arguments?.Count > 0 ? ProfileDescription.Arguments : defaultWorkerDescription.Arguments;
            defaultWorkerDescription.DefaultExecutablePath = string.IsNullOrEmpty(ProfileDescription.DefaultExecutablePath) ? defaultWorkerDescription.DefaultExecutablePath : ProfileDescription.DefaultExecutablePath;
            defaultWorkerDescription.DefaultWorkerPath = string.IsNullOrEmpty(ProfileDescription.DefaultWorkerPath) ? defaultWorkerDescription.DefaultWorkerPath : ProfileDescription.DefaultWorkerPath;
            defaultWorkerDescription.Extensions = ProfileDescription.Extensions?.Count > 0 ? ProfileDescription.Extensions : defaultWorkerDescription.Extensions;
            defaultWorkerDescription.Language = string.IsNullOrEmpty(ProfileDescription.Language) ? defaultWorkerDescription.Language : ProfileDescription.Language;
            defaultWorkerDescription.WorkerDirectory = string.IsNullOrEmpty(ProfileDescription.WorkerDirectory) ? defaultWorkerDescription.WorkerDirectory : ProfileDescription.WorkerDirectory;
            return defaultWorkerDescription;
        }
    }
}