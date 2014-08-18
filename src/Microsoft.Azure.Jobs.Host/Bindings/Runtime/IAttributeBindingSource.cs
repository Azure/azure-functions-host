// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Bindings.Runtime
{
    internal interface IAttributeBindingSource
    {
        AmbientBindingContext AmbientBindingContext { get; }

        Task<IBinding> BindAsync<TValue>(Attribute attribute, CancellationToken cancellationToken);
    }
}
