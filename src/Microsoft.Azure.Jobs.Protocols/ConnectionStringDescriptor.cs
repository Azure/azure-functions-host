// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Newtonsoft.Json;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a connection string used by a host instance.</summary>
    [JsonConverter(typeof(ConnectionStringDescriptorConverter))]
#if PUBLICPROTOCOL
    public class ConnectionStringDescriptor
#else
    internal class ConnectionStringDescriptor
#endif
    {
        /// <summary>Gets or sets the connection string type.</summary>
        public string Type { get; set; }

        private class ConnectionStringDescriptorConverter : PolymorphicJsonConverter
        {
            public ConnectionStringDescriptorConverter()
                : base("Type", PolymorphicJsonConverter.GetTypeMapping<ConnectionStringDescriptor>())
            {
            }
        }
    }
}
