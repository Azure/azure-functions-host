// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Common
{
    // Test triggering binding permutations using the FakeQueue mocks. 
    // - Single vs. Multiple
    // - native type (FakeQueue) vs. string vs poco 
    // - route parameters (binding contract)
    public class TriggerTests
    {
        // dummy Poco to use with test. 
        public class Payload
        {
            public int val1 { get; set; }
        }

         public class FunctionsSingle : FunctionsBase
        {
            public void SingleTrigger([FakeQueueTrigger] FakeQueueData single)
            {
                _collected.Add(single);
                this.Finished();
            }
        }
        public class FunctionMulti : FunctionsBase
        {
            public void MultipleTrigger([FakeQueueTrigger] FakeQueueData[] batch)
            {
                foreach (var item in batch)
                {
                    _collected.Add(item);
                }
                this.Finished();
            }
        }
                
        public class FunctionsByteArray : FunctionsBase
        {
            // If a Item-->byte[]  converter is registered with the ConverterManager, then 
            // that is invoked and this function receives a single Item. 
            // Else, assume array means batch, and this will receive an array of items serialized to Byte. 
            public void Trigger([FakeQueueTrigger] byte[] single)
            {
                _collected.Add(single);
                this.Finished();
            }
        }
                
        public class FunctionsDoubleByteArray : FunctionsBase
        {
            public void Trigger([FakeQueueTrigger] byte[][] single)
            {
                _collected.Add(single);
                this.Finished();
            }
        }

        class FunctionsSingleString : FunctionsBase
        {
            public void SingleStringTrigger([FakeQueueTrigger] string single)
            {
                _collected.Add(single);
                this.Finished();
            }
        }

        class FunctionsOutputUsesParams : FunctionsBase
        {
            public async Task SinglePocoTrigger([FakeQueueTrigger] Payload single, int val1,
                [FakeQueue(Prefix = "x{val1}")] IAsyncCollector<FakeQueueData> q2)
            {
                await q2.AddAsync(new FakeQueueData { Message = "abc"});

                this.Finished();
            }
        }

        class FunctionsSinglePoco : FunctionsBase
        {
            public void SinglePocoTrigger([FakeQueueTrigger] Payload single, int val1)
            {
                _collected.Add(val1);
                this.Finished();
            }
        }

        class FunctionsMultiPoco : FunctionsBase
        {
            public void SinglePocoTrigger([FakeQueueTrigger] Payload[] single)
            {
                _collected.AddRange(single);
                this.Finished();
            }
        }

        // help for test trigger functions to intergrate with tests.
        public class FunctionsBase
        {
            // Event to signal that the JobHost can stop polling. 
            public AutoResetEvent _stopEvent = new AutoResetEvent(false);
            protected void Finished()
            {
                _stopEvent.Set();
            }
            public List<object> _collected = new List<object>();
        }


        // Queue 2 native events to a single-dispatch trigger and ensure they both fire. 
        [Fact]
        public void TestSingleDispatch()
        {
            var e0 = new FakeQueueData
            {
                Message = "first",
                ExtraPropertery = "extra"
            };
            var e1 = new FakeQueueData
            {
                Message = "second",
                ExtraPropertery = "extra2"
            };
            var items = Run<FunctionsSingle>(e0, e1);
            Assert.Equal(2, items.Length);

            // For a direct binding, should be the same instance and skip serialization altogether. 
            Assert.True(object.ReferenceEquals(e0, items[0]));
            Assert.True(object.ReferenceEquals(e1, items[1]));
        }

        // Queue a batch 
        [Fact]
        public void TestMultiDispatch()
        {
            var e0 = new FakeQueueData
            {
                Message = "first",
                ExtraPropertery = "extra"
            };
            var e1 = new FakeQueueData
            {
                Message = "second",
                ExtraPropertery = "extra2"
            };
            var items = Run<FunctionMulti>(e0, e1);
            Assert.Equal(2, items.Length);

            // For a direct binding, should be the same instance and skip serialization altogether. 
            Assert.True(object.ReferenceEquals(e0, items[0]));
            Assert.True(object.ReferenceEquals(e1, items[1]));
        }
                
        [Fact]
        public void TestFunctionsOutputUsesParams()
        {
            var payload = new Payload { val1 = 123 };
            var e0 = new FakeQueueData
            {
                Message = JsonConvert.SerializeObject(payload)
            };

            var items = Run<FunctionsOutputUsesParams>(e0);
            Assert.Equal(1, items.Length);

            // this trigger strongly binds to a poco and adds Payload.val1
            var d = (FakeQueueData) (items[0]);
            Assert.Equal("x123", d.ExtraPropertery);
            Assert.Equal("abc", d.Message);
        }

        // Queue 2 native events to a single-dispatch trigger and ensure they both fire. 
        [Fact]
        public void TestSinglePocoDispatch()
        {
            var payload = new Payload { val1 = 123 };
            var e0 = new FakeQueueData
            {
                Message = JsonConvert.SerializeObject(payload)
            };

            var items = Run<FunctionsSinglePoco>(e0);
            Assert.Equal(1, items.Length);

            // this trigger strongly binds to a poco and adds Payload.val1
            Assert.Equal(payload.val1, items[0]);
        }

        // Queue 2 native events to a single-dispatch trigger and ensure they both fire. 
        [Fact]
        public void TestMultiPocoDispatch()
        {
            var payload0 = new Payload { val1 = 100 };
            var payload1 = new Payload { val1 = 200 };            
            var e0 = new FakeQueueData
            {
                Message = JsonConvert.SerializeObject(payload0)
            };
            var e1 = new FakeQueueData
            {
                Message = JsonConvert.SerializeObject(payload1)
            };

            var items = Run<FunctionsMultiPoco>(e0, e1);
            Assert.Equal(2, items.Length);

            // this trigger strongly binds to a poco and adds Payload.val1
            Assert.Equal(payload0.val1, ((Payload) items[0]).val1);
            Assert.Equal(payload1.val1, ((Payload) items[1]).val1);
        }

        // Queue 2 native events to a single-dispatch trigger and ensure they both fire. 
        [Fact]
        public void TestSingleStringDispatch()
        {
            var e0 = new FakeQueueData
            {
                Message = "xyz"
            };

            var items = Run<FunctionsSingleString>(e0);
            Assert.Equal(1, items.Length);

            // this trigger strongly binds to a poco and adds Payload.val1
            Assert.Equal(e0.Message, items[0]);
        }

        static void AddItem2ByteArrayConverter(IConverterManager cm)
        {
            cm.AddConverter<FakeQueueData, byte[]>(msg => System.Text.Encoding.UTF8.GetBytes(msg.Message));
        }
        static void AddItem2ByteConverter(IConverterManager cm)
        {
            cm.AddConverter<FakeQueueData, byte>(msg => msg.Byte);
        }

        // If a Item-->Byte[] converter is registered, 
        // then dispatch the Item as a single byte[] callback
        [Fact]
        public void TestByteArrayDispatch()
        {
            var e0 = new FakeQueueData
            {
                Message = "ABC"
            };

            var client = new FakeQueueClient();
            client.SetConverters = AddItem2ByteArrayConverter;
            
            var items = Run<FunctionsByteArray>(client, e0);
            Assert.Equal(1, items.Length);
            
            // This uses the Item --> byte[] converter. Dispatch as a single item.
            // Received as 1 object, a byte[]. 
            var bytes = System.Text.Encoding.UTF8.GetBytes(e0.Message);
            Assert.Equal(bytes, items[0]);
        }

        // If a Item-->Byte[] converter is registered, 
        // then dispatch a batch of Items as a single byte[][] callback
        [Fact]
        public void TestByteArrayDispatch3()
        {
            var client = new FakeQueueClient();
            client.SetConverters = AddItem2ByteArrayConverter;

            object[] items = Run<FunctionsDoubleByteArray>(client,
                new FakeQueueData { Message = "AB" },
                new FakeQueueData { Message = "CD" }
                );
            Assert.Equal(1, items.Length);

            var arg = (byte[][])(items[0]);

            Assert.Equal(new byte[] { 65, 66 }, arg[0]);
            Assert.Equal(new byte[] { 67, 68 }, arg[1]);
        }

        // IF a Item-->Byte converter is specified (not a byte[]), 
        // Then dispatch a batch of Items as a byte[]. 
        [Fact]
        public void TestByteArrayDispatch2()
        {
            var client = new FakeQueueClient();
            client.SetConverters = AddItem2ByteConverter;
            
            var items = Run<FunctionsByteArray>(client,
                new FakeQueueData { Byte = 1 },
                new FakeQueueData { Byte = 2 },
                new FakeQueueData { Byte = 3 }
                );
            Assert.Equal(1, items.Length);

            // Received as 1 batch, with 3 entries. 
            var bytes = new byte[] { 1, 2, 3 };
            Assert.Equal(bytes, items[0]);
        }

        // Helper to send items to the listener, and return what they collected
        private object[] Run<TFunction>(params FakeQueueData[] items) where TFunction : FunctionsBase, new()
        {
            return Run<TFunction>(new FakeQueueClient(), items);
        }

        private object[] Run<TFunction>(FakeQueueClient client, params FakeQueueData[] items) where TFunction : FunctionsBase, new()
        {        
            var activator = new FakeActivator();
            var func1 = new TFunction();
            activator.Add(func1);

            var host = TestHelpers.NewJobHost<TFunction>(client, activator);

            foreach (var item in items)
            {
                client.AddAsync(item).Wait();
            }

            host.Start();
            TestHelpers.WaitOne(func1._stopEvent);
            host.Stop();

            // Add any items sent using [FakeQueue(Prefix=...)]
            foreach (var kv in client._prefixedItems)
            {
                func1._collected.AddRange(kv.Value);
            }

            return func1._collected.ToArray();
        }
    }
}
