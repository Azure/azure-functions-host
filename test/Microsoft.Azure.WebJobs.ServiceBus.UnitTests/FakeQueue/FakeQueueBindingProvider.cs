// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Bindings;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests
{
    internal class FakeQueueBindingProvider : IBindingProvider
    {
        FakeQueueClient _client;
        IConverterManager _converterManager;

        public FakeQueueBindingProvider(FakeQueueClient client, IConverterManager converterManager)
        {
            _client = client;
            _converterManager = converterManager;
        }

        public Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;

            FakeQueueAttribute attribute = parameter.GetCustomAttribute<FakeQueueAttribute>(inherit: false);

            if (attribute == null)
            {
                return Task.FromResult<IBinding>(null);
            }

            string resolvedName = "fakequeue";
            Func<string, FakeQueueClient> invokeStringBinder = (invokeString) => _client;

            IBinding binding = GenericBinder.BindCollector<FakeQueueData, FakeQueueClient>(
                parameter,
                _converterManager,
                _client,
                (client, valueBindingContext) => client,
                resolvedName,
                invokeStringBinder
            );

            return Task.FromResult(binding);
        }
    }
}