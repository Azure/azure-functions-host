// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    public class RpcWorkerDescriptionProfile
    {
        private List<ProfileCondition> _conditions = new List<ProfileCondition>();

        /// <summary>
        /// Gets or sets the name of the profile.
        /// </summary>
        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the conditions which must be met for the profile to be used.
        /// </summary>
        [JsonProperty(PropertyName = "conditions")]
        public List<ProfileCondition> Conditions
        {
            get
            {
                return _conditions;
            }

            set
            {
                if (value != null)
                {
                    _conditions = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the worker description for the profile
        /// </summary>
        [JsonProperty(PropertyName = "description")]
        public RpcWorkerDescription ProfileDescription { get; set; }

        public bool Validate()
        {
            if (string.IsNullOrEmpty(Name))
            {
                return false;
                // throw new ValidationException($"WorkerDescriptionProfile {nameof(Name)} cannot be empty");
            }

            if ( Conditions == null || Conditions.Count > 1)
            {
                return false;
                // throw new ValidationException($"WorkerDescriptionProfile {nameof(Conditions)} cannot be empty");
            }

            if (ProfileDescription == null)
            {
                return false;
                // throw new ValidationException($"WorkerDescriptionProfile {nameof(ProfileDescription)} cannot be null");
            }
            return true;
        }
    }
}