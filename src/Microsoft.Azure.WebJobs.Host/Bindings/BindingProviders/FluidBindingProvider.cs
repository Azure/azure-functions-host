// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host
{
    // Base class to aide in private backwards compatability hooks for some bindings. 
    internal class FluidBindingProvider<TAttribute>
    {
        protected internal Func<TAttribute, ParameterInfo, INameResolver, ParameterDescriptor> BuildParameterDescriptor { get; set; }
        protected internal Func<TAttribute, ParameterInfo, INameResolver, Task<TAttribute>> PostResolveHook { get; set; }
    }
}