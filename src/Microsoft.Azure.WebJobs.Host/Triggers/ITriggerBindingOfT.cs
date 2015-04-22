// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;

namespace Microsoft.Azure.WebJobs.Host.Triggers
{
    internal interface ITriggerBinding<TTriggerValue> : ITriggerBinding
    {
        Task<ITriggerData> BindAsync(TTriggerValue value, ValueBindingContext context);
    }
}
