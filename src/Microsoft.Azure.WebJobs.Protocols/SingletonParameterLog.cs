// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Newtonsoft.Json;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.WebJobs.Protocols
#else
namespace Microsoft.Azure.WebJobs.Host.Protocols
#endif
{
    /// <summary>
    /// Represents a function parameter log for a virtual singleton parameter.
    /// </summary>
    [JsonTypeName("Singleton")]
#if PUBLICPROTOCOL
    public class SingletonParameterLog : ParameterLog
#else
    internal class SingletonParameterLog : ParameterLog
#endif
    {
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public TimeSpan? TimeToAcquireLock { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public TimeSpan? LockDuration { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string LockOwner { get; set; }

        public bool LockAcquired { get; set; }
    }
}
