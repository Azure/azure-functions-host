// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Queue;

#if PUBLICSTORAGE
namespace Microsoft.Azure.Jobs.Storage.Queue
#else
namespace Microsoft.Azure.Jobs.Host.Storage.Queue
#endif
{
    /// <summary>Defines a queue.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public interface ICloudQueue
#else
    internal interface ICloudQueue
#endif
    {
        /// <summary>Adds a message to the queue.</summary>
        /// <param name="message">The message to enqueue.</param>
        void AddMessage(CloudQueueMessage message);

        /// <summary>Creates the queue if it does not already exist.</summary>
        void CreateIfNotExists();
    }
}
