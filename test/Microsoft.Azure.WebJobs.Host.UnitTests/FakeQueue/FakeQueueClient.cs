// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Triggers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    // For sending fake queue messages. 
    public class FakeQueueClient : IExtensionConfigProvider, IConverter<FakeQueueAttribute, FakeQueueClient>
    {
        public List<FakeQueueData> _items = new List<FakeQueueData>();

        public Dictionary<string, List<FakeQueueData>> _prefixedItems = new Dictionary<string, List<FakeQueueData>>();

        public Task AddAsync(FakeQueueData item, CancellationToken cancellationToken = default(CancellationToken))
        {
            _items.Add(item);
            return Task.FromResult(0);
        }

        public Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            // Batching not supported. 
            return Task.FromResult(0);
        }

        // Test hook for customizing converters
        public Action<IConverterManager> SetConverters
        {
            get; set;
        }

        void IExtensionConfigProvider.Initialize(ExtensionConfigContext context)
        {
            INameResolver nameResolver = context.Config.GetService<INameResolver>();
            IConverterManager cm = context.Config.GetService<IConverterManager>();
            cm.AddConverter<string, FakeQueueData>(x => new FakeQueueData { Message = x });

            if (this.SetConverters != null)
            {
                this.SetConverters(cm);
            }
            
            cm.AddConverter<FakeQueueData, string>(msg => msg.Message);
            cm.AddConverter<OtherFakeQueueData, FakeQueueData>(OtherFakeQueueData.ToEvent);

            IExtensionRegistry extensions = context.Config.GetService<IExtensionRegistry>();

            var bf = new BindingFactory(nameResolver, cm);

            // Binds [FakeQueue] --> IAsyncCollector<FakeQueueData>
            var ruleOutput = bf.BindToCollector<FakeQueueAttribute, FakeQueueData>(BuildFromAttr);

            // Binds [FakeQueue] --> FakeQueueClient            
            var ruleClient = bf.BindToInput<FakeQueueAttribute, FakeQueueClient>(this);

            extensions.RegisterBindingRules<FakeQueueAttribute>(ruleOutput, ruleClient);

            var triggerBindingProvider = new FakeQueueTriggerBindingProvider(this, cm);
            extensions.RegisterExtension<ITriggerBindingProvider>(triggerBindingProvider);
        }

        FakeQueueClient IConverter<FakeQueueAttribute, FakeQueueClient>.Convert(FakeQueueAttribute attr)
        {
            return this;
        }

        private IAsyncCollector<FakeQueueData> BuildFromAttr(FakeQueueAttribute attr)
        {
            // Caller already resolved anything. 
            return new Myqueue
            {
                _parent = this,
                _prefix = attr.Prefix
            }; 

        }

        public IAsyncCollector<FakeQueueData> GetQueue()
        {
            return new Myqueue
            {
                _parent = this
            };
        }

        class Myqueue : IAsyncCollector<FakeQueueData>
        {
            internal FakeQueueClient _parent;
            internal string _prefix;

            public async Task AddAsync(FakeQueueData item, CancellationToken cancellationToken = default(CancellationToken))
            {
                if (_prefix != null)
                {
                    // Add these to a look-aside buffer. Won't trigger further  
                    item.ExtraPropertery = _prefix;
                    List<FakeQueueData> l;
                    if (!_parent._prefixedItems.TryGetValue(_prefix, out l))
                    {
                        l = new List<FakeQueueData>();
                        _parent._prefixedItems[_prefix] = l;
                    }
                    l.Add(item);
                }
                else
                {
                    await _parent.AddAsync(item);
                }
            }

            public Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken))
            {
                return _parent.FlushAsync();
            }
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