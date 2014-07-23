// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Defines a persistent queue reader.</summary>
    /// <typeparam name="T">The type of messages in the queue.</typeparam>
#if PUBLICPROTOCOL
    public interface    IPersistentQueueReader<T>
#else
    internal interface IPersistentQueueReader<T>
#endif
    {
        /// <summary>Dequeues the next message in the queue, if any.</summary>
        /// <returns>The dequeued message, if any.</returns>
        /// <remarks>Dequeuing marks the message as temorarly invisible.</remarks>
        T Dequeue();

        /// <summary>Deletes a message from the queue.</summary>
        /// <param name="message">The message to delete.</param>
        void Delete(T message);
    }
}
