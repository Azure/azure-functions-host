// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    internal class ExternalFunctionsServiceProviderFactory : IServiceProviderFactory<FunctionsServiceProvider>
    {
        private FunctionsServiceProvider _provider;

        public ExternalFunctionsServiceProviderFactory(FunctionsServiceProvider provider)
        {
            _provider = provider;
        }

        public FunctionsServiceProvider CreateBuilder(IServiceCollection services)
        {
            _provider.UpdateChildServices(services);
            return _provider;
        }

        public IServiceProvider CreateServiceProvider(FunctionsServiceProvider containerBuilder)
        {
            return containerBuilder;
        }
    }
}
