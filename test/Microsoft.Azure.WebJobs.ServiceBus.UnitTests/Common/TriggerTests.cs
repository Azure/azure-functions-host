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

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests.Common
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

        class FunctionsSingleString : FunctionsBase
        {
            public void SingleStringTrigger([FakeQueueTrigger] string single)
            {
                _collected.Add(single);
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

        // Helper to send items to the listener, and return what they collected
        private object[] Run<TFunction>(params FakeQueueData[] items) where TFunction : FunctionsBase, new()
        {
            var activator = new FakeActivator();
            var func1 = new TFunction();
            activator.Add(func1);

            JobHostConfiguration config = new JobHostConfiguration()
            {
                TypeLocator = new FakeTypeLocator(typeof(TFunction)),
                JobActivator = activator
            };

            FakeQueueClient client = new FakeQueueClient();
            IExtensionRegistry extensions = config.GetService<IExtensionRegistry>();
            extensions.RegisterExtension<IExtensionConfigProvider>(client);

            JobHost host = new JobHost(config);

            foreach (var item in items)
            {
                client.AddAsync(item).Wait();
            }

            host.Start();
            TestHelpers.WaitOne(func1._stopEvent);
            host.Stop();

            return func1._collected.ToArray();
        }
    }
}
