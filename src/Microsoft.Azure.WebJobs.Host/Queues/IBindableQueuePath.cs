// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Bindings;

namespace Microsoft.Azure.WebJobs.Host.Queues
{
    interface IBindableQueuePath : IBindablePath<string>
    {
        string QueueNamePattern { get; }
    }
}
