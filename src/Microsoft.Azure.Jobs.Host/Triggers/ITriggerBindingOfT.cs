// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal interface ITriggerBinding<TTriggerValue> : ITriggerBinding
    {
        Task<ITriggerData> BindAsync(TTriggerValue value, FunctionBindingContext context);
    }
}
