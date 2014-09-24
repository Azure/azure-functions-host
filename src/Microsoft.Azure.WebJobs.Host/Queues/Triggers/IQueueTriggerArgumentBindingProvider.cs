// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Reflection;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Triggers;

namespace Microsoft.Azure.WebJobs.Host.Queues.Triggers
{
    internal interface IQueueTriggerArgumentBindingProvider
    {
        ITriggerDataArgumentBinding<IStorageQueueMessage> TryCreate(ParameterInfo parameter);
    }
}
