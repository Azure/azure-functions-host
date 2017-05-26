// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Triggers;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    internal class FakeQueueTriggerBindingProvider : ITriggerBindingProvider
    {
        private readonly IConverterManager _converterManager;
        FakeQueueClient _client;

        public FakeQueueTriggerBindingProvider(FakeQueueClient client, IConverterManager converterManager)
        {
            _client = client;
            _converterManager = converterManager;
        }

        public Task<ITriggerBinding> TryCreateAsync(TriggerBindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            FakeQueueTriggerAttribute attribute = parameter.GetCustomAttribute<FakeQueueTriggerAttribute>(inherit: false);

            if (attribute == null)
            {
                return Task.FromResult<ITriggerBinding>(null);
            }

            var hooks = new FakeQueueTriggerBindingStrategy();

            Func<ListenerFactoryContext, bool, Task<IListener>> createListener =
                (factoryContext, singleDispatch) =>
                {
                    IListener listener = new FakeQueueListener(factoryContext.Executor, _client, singleDispatch);
                    return Task.FromResult(listener);
                };

            ITriggerBinding binding = BindingFactory.GetTriggerBinding<FakeQueueData, FakeQueueDataBatch>(
                hooks, parameter, _converterManager, createListener);

            return Task.FromResult<ITriggerBinding>(binding);
        }
    }
}