// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Reflection;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.Azure.ServiceBus;

namespace Microsoft.Azure.WebJobs.ServiceBus.Triggers
{
    internal interface IQueueTriggerArgumentBindingProvider
    {
        ITriggerDataArgumentBinding<Message> TryCreate(ParameterInfo parameter);
    }
}
