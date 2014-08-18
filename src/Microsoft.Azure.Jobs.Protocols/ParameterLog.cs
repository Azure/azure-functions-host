// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Newtonsoft.Json;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.WebJobs.Protocols
#else
namespace Microsoft.Azure.WebJobs.Host.Protocols
#endif
{
    /// <summary>Represents a function parameter log.</summary>
    [JsonConverter(typeof(ParameterLogConverter))]
#if PUBLICPROTOCOL
    public class ParameterLog
#else
    internal class ParameterLog
#endif
    {
        /// <summary>Gets or sets the log type.</summary>
        public string Type { get; set; }

        private class ParameterLogConverter : PolymorphicJsonConverter
        {
            public ParameterLogConverter()
                : base("Type", PolymorphicJsonConverter.GetTypeMapping<ParameterLog>())
            {
            }
        }
    }
}
