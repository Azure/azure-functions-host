// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    //[JsonConverter(typeof(WorkerDescriptionProfileConverter))]
    public class WorkerDescriptionProfile
    {
        /// <summary>
        /// Gets or sets the name of the profile.
        /// </summary>
        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the conditions which must be met for the profile to be used.
        /// </summary>
        [JsonProperty(PropertyName = "conditions")]
        public List<IWorkerProfileCondition> Conditions { get; set; }

        /// <summary>
        /// Gets or sets the worker description for the profile
        /// </summary>
        [JsonProperty(PropertyName = "description")]
        public RpcWorkerDescription ProfileDescription { get; set; }

        public void Validate()
        {
            // type name and expression
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
    }
}