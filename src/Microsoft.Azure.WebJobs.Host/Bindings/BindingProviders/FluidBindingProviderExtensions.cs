// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host
{    
    // Internal Extension methods for setting backwards compatibility hooks on certain bindings. 
    // This keeps the hooks out of the public surface. 
    internal static class FluidBindingProviderExtensions
    {
        public static IBindingProvider SetPostResolveHook<TAttribute>(
            this IBindingProvider binder,
            Func<TAttribute, ParameterInfo, INameResolver, ParameterDescriptor> buildParameterDescriptor = null,
            Func<TAttribute, ParameterInfo, INameResolver, Task<TAttribute>> postResolveHook = null)
        {
            var fluidBinder = (FluidBindingProvider<TAttribute>)binder;

            fluidBinder.PostResolveHook = postResolveHook;
            fluidBinder.BuildParameterDescriptor = buildParameterDescriptor;
            return binder;
        }
    }
}