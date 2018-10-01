﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
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
            ChildScopes = new ConcurrentDictionary<ServiceScope, object>();
        }

        public IContainer Container { get; }

        public ConcurrentDictionary<ServiceScope, object> ChildScopes { get; }

        public bool IsRootResolver { get; }

        public void Dispose()
        {
            Task childScopeTasks = Task.WhenAll(ChildScopes.Keys.Select(s => s.DisposalTask));
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
            ChildScopes.TryAdd(scope, null);

            scope.DisposalTask.ContinueWith(t => ChildScopes.TryRemove(scope, out object _));

            return scope;
        }
    }
}
