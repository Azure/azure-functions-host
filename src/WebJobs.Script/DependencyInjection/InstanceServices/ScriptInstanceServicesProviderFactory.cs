// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Host;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.DependencyInjection
{
    internal class ScriptInstanceServicesProviderFactory : IInstanceServicesProviderFactory
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public ScriptInstanceServicesProviderFactory(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }

        public IInstanceServicesProvider CreateInstanceServicesProvider(FunctionInstanceFactoryContext functionInstance)
        {
            if (functionInstance.Parameters is ScriptInvocationArguments scriptArguments
                && scriptArguments.ServiceProvider is not null)
            {
                return new ScriptInstanceServicesProvider(scriptArguments.ServiceProvider);
            }

            return new ScriptInstanceServicesProvider(_serviceScopeFactory);
        }
    }
}
