// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using DryIoc;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    public class ServiceScope : IServiceScope
    {
        private readonly TaskCompletionSource<object> _activeTcs;
        private readonly ScopedServiceProvider _serviceProvider;
        private readonly IServiceScope _rootScope;

        public ServiceScope(IResolverContext serviceProvider, IServiceScope rootScope)
        {
            _activeTcs = new TaskCompletionSource<object>();
            _serviceProvider = new ScopedServiceProvider(serviceProvider);
            _rootScope = rootScope;
        }

        public IServiceProvider ServiceProvider => _serviceProvider;

        public Task DisposalTask => _activeTcs.Task;

        public void Dispose()
        {
            _serviceProvider.Dispose();
            _rootScope.Dispose();
            _activeTcs.SetResult(null);
        }
    }
}
