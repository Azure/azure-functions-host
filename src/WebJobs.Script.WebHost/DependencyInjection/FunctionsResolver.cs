// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DryIoc;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    internal class FunctionsResolver : IDisposable
    {
        public FunctionsResolver(IContainer resolver, bool isRootResolver = false)
        {
            Container = resolver ?? throw new ArgumentNullException(nameof(resolver));
            IsRootResolver = isRootResolver;
            ChildScopes = new HashSet<FunctionsServiceScope>();
        }

        public IContainer Container { get; }

        public HashSet<FunctionsServiceScope> ChildScopes { get; }

        public bool IsRootResolver { get; }

        public void Dispose()
        {
            Task childScopeTasks = Task.WhenAll(ChildScopes.Select(s => s.DisposalTask));
            Task.WhenAny(childScopeTasks, Task.Delay(5000))
                .ContinueWith(t =>
                {
                    Container.Dispose();
                });
        }

        internal FunctionsServiceScope CreateChildScope()
        {
            IResolverContext scopedContext = Container.OpenScope();
            var scope = new FunctionsServiceScope(scopedContext);
            ChildScopes.Add(scope);

            scope.DisposalTask.ContinueWith(t => ChildScopes.Remove(scope));

            return scope;
        }
    }
}
