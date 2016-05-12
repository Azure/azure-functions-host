// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Config;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System;
using Xunit;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Newtonsoft.Json;
using Microsoft.Azure.WebJobs.Host.UnitTests.Indexers;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    // Excercise the dynamc IAsyncCollector<T> binding rule, where T
    // is determined based on the user function's parameter type. 
    // This skips the IConverterManager. 
    public class TypedCollectorTests
    {
        // An arbtirary foreign type. 
        public class SpecialData
        {
            [JsonIgnore] // Esnure the object is not serialized. 
            public string Message;
        }
        // Various flavors that all bind down to an IAsyncCollector 
        public class Functions
        {
            public static void T1(
                [FakeQueue] IAsyncCollector<SpecialData> qAsync,
                [FakeQueue(Prefix = "%appsetting1%")] ICollector<SpecialData> qSync,
                [FakeQueue] out SpecialData q2, // other bindings
                [FakeQueue] out SpecialData[] q3, // other bindings
                [FakeQueue] IAsyncCollector<DateTime> qStruct)
            {
                // Collectors are queued immediatley. 
                qAsync.AddAsync(new SpecialData { Message = "q1a" }).Wait();
                qSync.Add(new SpecialData { Message = "q1b" });

                // Test queuing a struct. 
                qStruct.AddAsync(DateTime.MaxValue).Wait();

                // Out parameters are queued afte rthe function returns. 
                q2 = new SpecialData { Message = "q2" };
                q3 = new SpecialData[] {
                     new SpecialData { Message = "q3a" },
                     new SpecialData { Message = "q3b" }
                };    
            }

            public static void ObjectArray(
              [FakeQueue] out object[] qobjc)
            {
                qobjc = new SpecialData[] {
                      new SpecialData { Message = "1" },
                      new SpecialData { Message = "2" },
                };
            }
        }

        [Fact]
        public void TestObjectArray()
        {
            var client = new FakeQueueTypedClient();
            var nr = new DictNameResolver();
            nr.Add("appsetting1", "val1");
            var host = TestHelpers.NewJobHost<Functions>(client, nr);

            host.Call("ObjectArray");

            Assert.Equal(2, client._items.Count);
            Assert_IsSpecialData("1", client._items[0]);
            Assert_IsSpecialData("2", client._items[1]);
            client._items.Clear();
        }            
        
        [Fact]
        public void Test()
        {
            var client = new FakeQueueTypedClient();
            var nr = new DictNameResolver();
            nr.Add("appsetting1", "val1");
            var host = TestHelpers.NewJobHost<Functions>(client, nr);

            host.Call("T1");

            Assert.Equal(6, client._items.Count);

            Assert_IsSpecialData("q1a", client._items[0]);
            Assert_IsSpecialData("val1:q1b", client._items[1]); // attr has prefix 
            Assert_Type<DateTime>(DateTime.MaxValue, client._items[2]);
            Assert_IsSpecialData("q2", client._items[3]);
            Assert_IsSpecialData("q3a", client._items[4]);
            Assert_IsSpecialData("q3b", client._items[5]);
        }

        private static void Assert_Type<T>(T expected, object o)
        {
            var x = (T)o;
            Assert.Equal(expected, x);
        }

        private static void Assert_IsSpecialData(string msg, object o)
        {
            var x = (SpecialData)o;
            Assert.Equal(msg, x.Message);
        }
    }

    public class FakeQueueTypedClient : IExtensionConfigProvider
    {
        // Track items that are queued. 
        public List<object> _items;
        public string _prefix; // from attribute, to test attribute automatic resolution. 

        public FakeQueueTypedClient()
        {
            _items = new List<object>();
        }
        public FakeQueueTypedClient(FakeQueueTypedClient inner, string prefix)
        {
            _items = inner._items;
            _prefix = prefix;
        }

        void IExtensionConfigProvider.Initialize(ExtensionConfigContext context)
        {
            INameResolver nameResolver = context.Config.GetService<INameResolver>();
            IConverterManager cm = context.Config.GetService<IConverterManager>();
            // Don't add any converters. 
            IExtensionRegistry extensions = context.Config.GetService<IExtensionRegistry>();

            var bf = new BindingFactory(nameResolver, cm);

            var ruleOutput = bf.BindToGenericAsyncCollector<FakeQueueAttribute, FakeQueueTypedClient>(
                typeof(TypedAsyncCollector<>), (attr) => new FakeQueueTypedClient(this, attr.Prefix));

            extensions.RegisterBindingRules<FakeQueueAttribute>(ruleOutput);
        }


        public class TypedAsyncCollector<T> : IAsyncCollector<T>
        {
            private readonly FakeQueueTypedClient _client;

            public TypedAsyncCollector(FakeQueueTypedClient client)
            {
                _client = client;
            }

            public Task AddAsync(T item, CancellationToken cancellationToken = default(CancellationToken))
            {
                var x = item as TypedCollectorTests.SpecialData;
                if (x != null)
                {
                    if (_client._prefix != null)
                    {
                        x.Message = _client._prefix + ":" + x.Message;
                    }
                }

                _client._items.Add(item);
                return Task.FromResult(0);
            }

            public Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken))
            {
                return Task.FromResult(0);          
            }
        }
    }
}