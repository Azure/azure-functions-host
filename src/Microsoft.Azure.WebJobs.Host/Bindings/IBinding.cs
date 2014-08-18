// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    internal interface IBinding
    {
        bool FromAttribute { get; }

        Task<IValueProvider> BindAsync(object value, ValueBindingContext context);

        Task<IValueProvider> BindAsync(BindingContext context);

        ParameterDescriptor ToParameterDescriptor();
    }
}
