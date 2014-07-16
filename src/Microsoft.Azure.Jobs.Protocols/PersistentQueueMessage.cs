// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a message in a persistent queue.</summary>
    [JsonConverter(typeof(PersistentQueueMessageConverter))]
#if PUBLICPROTOCOL
    public abstract class PersistentQueueMessage
#else
    internal abstract class PersistentQueueMessage
#endif
    {
        /// <summary>Gets or sets the message type.</summary>
        public string Type { get; set; }

        /// <summary>Gets or sets the time the message was enqueued.</summary>
        [JsonIgnore]
        public DateTimeOffset EnqueuedOn { get; set; }

        /// <summary>Gets or sets a receipt from dequeuing the message.</summary>
        [JsonIgnore]
        public string PopReceipt { get; set; }

        private class PersistentQueueMessageConverter : PolymorphicJsonConverter
        {
            public PersistentQueueMessageConverter()
                : base("Type", PolymorphicJsonConverter.GetTypeMapping<PersistentQueueMessage>())
            {
            }
        }

        internal abstract void AddMetadata(IDictionary<string, string> metadata);
    }
}
