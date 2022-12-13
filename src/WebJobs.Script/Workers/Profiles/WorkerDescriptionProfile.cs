// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Workers.Profiles
{
    /// <summary>
    /// Class that holds data of a profile
    /// </summary>
    public class WorkerDescriptionProfile
    {
        public WorkerDescriptionProfile(string name, List<IWorkerProfileCondition> conditions, RpcWorkerDescription profileDescription)
        {
            Name = name;
            Conditions = conditions;
            ProfileDescription = profileDescription;
            ProfileId = Guid.NewGuid().ToString();
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

        public string ProfileId { get; }

        private void Validate()
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
        /// Creates a new worker description and overrides parameters with those available in the profile
        /// </summary>
        public RpcWorkerDescription ApplyProfile(RpcWorkerDescription defaultWorkerDescription)
        {
            RpcWorkerDescription updatedDescription = new();
            updatedDescription.Arguments = UseProfileOrDefault(ProfileDescription.Arguments, defaultWorkerDescription.Arguments);
            updatedDescription.DefaultExecutablePath = UseProfileOrDefault(ProfileDescription.DefaultExecutablePath, defaultWorkerDescription.DefaultExecutablePath);
            updatedDescription.DefaultWorkerPath = UseProfileOrDefault(ProfileDescription.DefaultWorkerPath, defaultWorkerDescription.DefaultWorkerPath);
            updatedDescription.Extensions = UseProfileOrDefault(ProfileDescription.Extensions, defaultWorkerDescription.Extensions) as List<string>;
            updatedDescription.Language = UseProfileOrDefault(ProfileDescription.Language, defaultWorkerDescription.Language);
            updatedDescription.WorkerDirectory = UseProfileOrDefault(ProfileDescription.WorkerDirectory, defaultWorkerDescription.WorkerDirectory);
            return updatedDescription;
        }

        private string UseProfileOrDefault(string profileParameter, string defaultParameter)
        {
            return string.IsNullOrEmpty(profileParameter) ? defaultParameter : profileParameter;
        }

        private IList<string> UseProfileOrDefault(IList<string> profileParameter, IList<string> defaultParameter)
        {
            return profileParameter != null && profileParameter.Count > 0 ? profileParameter : defaultParameter;
        }
    }
}