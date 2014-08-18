// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Queue
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Queue
#endif
{
    /// <summary>Defines a queue client.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public interface ICloudQueueClient
#else
    internal interface ICloudQueueClient
#endif
    {
        /// <summary>Gets a queue reference.</summary>
        /// <param name="queueName">The queue name.</param>
        /// <returns>A queue reference.</returns>
        ICloudQueue GetQueueReference(string queueName);
    }
}
