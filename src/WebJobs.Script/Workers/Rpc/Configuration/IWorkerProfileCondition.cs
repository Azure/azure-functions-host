// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public interface IWorkerProfileCondition
    {
        /// <summary>
        /// Gets the type of the condition
        /// </summary>
        [JsonProperty(PropertyName = "type")]
        public ConditionType Type { get; }

        bool Evaluate();
    }
}