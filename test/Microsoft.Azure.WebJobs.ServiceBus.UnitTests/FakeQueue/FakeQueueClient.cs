// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Triggers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests
{
    // For sending fake queue messages. 
    public class FakeQueueClient : IFlushCollector<FakeQueueData>, IExtensionConfigProvider
    {
        public List<FakeQueueData> _items = new List<FakeQueueData>();

        public Task AddAsync(FakeQueueData item, CancellationToken cancellationToken = default(CancellationToken))
        {
            _items.Add(item);
            return Task.FromResult(0);
        }

        public Task FlushAsync()
        {
            return Task.FromResult(0);
        }

        void IExtensionConfigProvider.Initialize(ExtensionConfigContext context)
        {
            IConverterManager cm = context.Config.GetOrCreateConverterManager();
            cm.AddConverter<string, FakeQueueData>(x => new FakeQueueData { Message = x });
            cm.AddConverter<FakeQueueData, string>(msg => msg.Message);
            cm.AddConverter<OtherFakeQueueData, FakeQueueData>(OtherFakeQueueData.ToEvent);

            IExtensionRegistry extensions = context.Config.GetService<IExtensionRegistry>();

            var bindingProvider = new FakeQueueBindingProvider(this, cm);
            extensions.RegisterExtension<IBindingProvider>(bindingProvider);

            var triggerBindingProvider = new FakeQueueTriggerBindingProvider(this, cm);
            extensions.RegisterExtension<ITriggerBindingProvider>(triggerBindingProvider);
        }
    }

    // A Test class that's not related to FakeQueueData, but can be converted to/from it. 
    public class OtherFakeQueueData
    {
        public string _test;

        public static FakeQueueData ToEvent(OtherFakeQueueData x)
        {
            return new FakeQueueData
            {
                ExtraPropertery = x._test
            };
        }
    }
}