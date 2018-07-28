// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DryIoc;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    internal class ScopedResolver : IDisposable
    {
        public ScopedResolver(IContainer resolver, bool isRootResolver = false)
        {
            Container = resolver ?? throw new ArgumentNullException(nameof(resolver));
            IsRootResolver = isRootResolver;
            ChildScopes = new HashSet<ServiceScope>();
        }

        public IContainer Container { get; }

        public HashSet<ServiceScope> ChildScopes { get; }

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

        internal ServiceScope CreateChildScope(IServiceScopeFactory rootScopeFactory)
        {
            var scopedRoot = rootScopeFactory.CreateScope();
            Container scopedContext = Container.OpenScope() as Container;

            Rules rules = scopedContext.Rules;
            foreach (var unknownServiceResolver in scopedContext.Rules.UnknownServiceResolvers)
            {
                rules = rules.WithoutUnknownServiceResolver(unknownServiceResolver);
            }

            var resolver = scopedContext.With(r => rules.WithUnknownServiceResolvers(request =>
              {
                  return new DelegateFactory(_ => scopedRoot.ServiceProvider.GetService(request.ServiceType));
              }));

            var scope = new ServiceScope(resolver, scopedRoot);
            ChildScopes.Add(scope);

            scope.DisposalTask.ContinueWith(t => ChildScopes.Remove(scope));

            return scope;
        }
    }
}
