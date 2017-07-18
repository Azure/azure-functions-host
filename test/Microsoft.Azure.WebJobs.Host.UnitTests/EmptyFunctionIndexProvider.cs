// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    internal class EmptyFunctionIndexProvider : IFunctionIndexProvider
    {
        public Task<IFunctionIndex> GetAsync(CancellationToken cancellationToken)
        {
            IFunctionIndex index = new EmptyFunctionIndex();
            return Task.FromResult(index);
        }

        private class EmptyFunctionIndex : IFunctionIndex
        {
            public IFunctionDefinition Lookup(string functionId)
            {
                return null;
            }

            public IFunctionDefinition Lookup(MethodInfo method)
            {
                return null;
            }

            public IFunctionDefinition LookupByName(string name)
            {
                return null;
            }

            public IEnumerable<IFunctionDefinition> ReadAll()
            {
                return Enumerable.Empty<IFunctionDefinition>();
            }

            public IEnumerable<FunctionDescriptor> ReadAllDescriptors()
            {
                return Enumerable.Empty<FunctionDescriptor>();
            }

            public IEnumerable<MethodInfo> ReadAllMethods()
            {
                return Enumerable.Empty<MethodInfo>();
            }
        }
    }
}
