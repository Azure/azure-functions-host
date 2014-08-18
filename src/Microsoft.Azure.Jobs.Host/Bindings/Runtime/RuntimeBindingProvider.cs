// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Bindings.Runtime
{
    internal class RuntimeBindingProvider : IBindingProvider
    {
        public Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;

            if (parameter.ParameterType != typeof(IBinder))
            {
                return Task.FromResult<IBinding>(null);
            }

            IBinding binding = new RuntimeBinding(parameter.Name);
            return Task.FromResult(binding);
        }
    }
}
