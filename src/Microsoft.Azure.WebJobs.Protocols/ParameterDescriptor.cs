// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.WebJobs.Protocols
#else
namespace Microsoft.Azure.WebJobs.Host.Protocols
#endif
{
    /// <summary>Represents a parameter to an Azure WebJobs SDK function.</summary>
    [JsonConverter(typeof(ParameterDescriptorConverter))]
#if PUBLICPROTOCOL
    public class ParameterDescriptor
#else
    internal class ParameterDescriptor
#endif
    {
        /// <summary>Gets or sets the parameter type.</summary>
        public string Type { get; set; }

        /// <summary>Gets or sets the parameter name.</summary>
        public string Name { get; set; }

        private class ParameterDescriptorConverter : PolymorphicJsonConverter
        {
            public ParameterDescriptorConverter()
                : base("Type", PolymorphicJsonConverter.GetTypeMapping<ParameterDescriptor>())
            {
            }
        }
    }
}
