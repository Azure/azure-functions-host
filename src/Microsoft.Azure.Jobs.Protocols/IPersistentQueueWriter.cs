// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Defines a persistent queue writer.</summary>
    /// <typeparam name="T">The type of messages in the queue.</typeparam>
#if PUBLICPROTOCOL
    public interface IPersistentQueueWriter<T>
#else
    internal interface IPersistentQueueWriter<T>
#endif
    {
        /// <summary>Adds a message to the queue.</summary>
        /// <param name="message">The message to enqueue.</param>
        /// <returns>The enqueued message identifier.</returns>
        string Enqueue(T message);

        /// <summary>Deletes a message from the queue.</summary>
        /// <param name="messageId">The message identifier from the message to delete.</param>
        void Delete(string messageId);
    }
}
