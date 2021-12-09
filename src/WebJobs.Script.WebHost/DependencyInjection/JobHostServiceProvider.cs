// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    public class JobHostServiceProvider : IServiceProvider, IServiceScopeFactory, ISupportRequiredService, IDisposable
    {
        private readonly IServiceCollection _descriptors;
        private readonly IServiceProvider _rootProvider;
        private IServiceProvider _serviceProvider;

        public JobHostServiceProvider(IServiceCollection descriptors, IServiceProvider rootProvider)
        {
            ArgumentNullException.ThrowIfNull(descriptors);
            ArgumentNullException.ThrowIfNull(rootProvider);
            _descriptors = descriptors;
            _rootProvider = rootProvider;
        }

        public IServiceProvider ServiceProvider => _serviceProvider ??= CreateServiceProvider();

        private IServiceProvider CreateServiceProvider()
        {
            var serviceCollection = _rootProvider.CreateChildContainer(_descriptors);
            return serviceCollection.BuildServiceProvider();
        }

        public object GetService(Type serviceType) => ServiceProvider.GetService(serviceType);

        public object GetRequiredService(Type serviceType) => ServiceProvider.GetRequiredService(serviceType);

        public IServiceScope CreateScope() => ServiceProvider.CreateScope();

        public void Dispose() => (ServiceProvider as IDisposable)?.Dispose();
    }
}
