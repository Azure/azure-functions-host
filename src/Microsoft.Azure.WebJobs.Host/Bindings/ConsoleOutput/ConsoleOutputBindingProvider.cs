// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Bindings.ConsoleOutput
{
    internal class ConsoleOutputBindingProvider : IBindingProvider
    {
        public Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;

            if (parameter.ParameterType != typeof(TextWriter))
            {
                return Task.FromResult<IBinding>(null);
            }

            IBinding binding = new ConsoleOutputBinding(parameter.Name);
            return Task.FromResult(binding);
        }
    }
}
