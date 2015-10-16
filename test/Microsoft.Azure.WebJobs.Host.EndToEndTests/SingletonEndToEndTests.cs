// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.EndToEndTests
{
    public class SingletonEndToEndTests : IClassFixture<SingletonEndToEndTests.TestFixture>
    {
        private const string TestArtifactsPrefix = "singletone2e";
        private const string Queue1Name = TestArtifactsPrefix + "q1%rnd%";
        private const string Queue2Name = TestArtifactsPrefix + "q2%rnd%";
        private const string Secondary = "SecondaryStorage";
        private Random _rand = new Random(314159);

        private static RandomNameResolver _resolver = new TestNameResolver();
        private static CloudBlobDirectory _lockDirectory;
        private static CloudBlobDirectory _secondaryLockDirectory;

        static SingletonEndToEndTests()
        {
            JobHostConfiguration config = new JobHostConfiguration();
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(config.StorageConnectionString);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            _lockDirectory = blobClient.GetContainerReference("azure-webjobs-hosts").GetDirectoryReference("locks");

            string secondaryConnectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(Secondary);
            storageAccount = CloudStorageAccount.Parse(secondaryConnectionString);
            blobClient = storageAccount.CreateCloudBlobClient();
            _secondaryLockDirectory = blobClient.GetContainerReference("azure-webjobs-hosts").GetDirectoryReference("locks");
        }

        public SingletonEndToEndTests()
        {
            TestJobs.Reset();
            TestTriggerAttributeBindingProvider.TestTriggerBinding.TestTriggerListener.StartCount = 0;
        }

        [Fact]
        public async Task SingletonNonTriggeredFunction_MultipleConcurrentInvocations_InvocationsAreSerialized()
        {
            JobHost host = CreateTestJobHost(1);
            host.Start();

            // make a bunch of parallel invocations
            int numInvocations = 20;
            List<Task> invokeTasks = new List<Task>();
            MethodInfo method = typeof(TestJobs).GetMethod("SingletonJob");
            for (int i = 0; i < numInvocations; i++)
            {
                int zone = _rand.Next(3) + 1;

                WorkItem workItem = new WorkItem
                {
                    ID = i + 1,
                    Region = "Central",
                    Zone = zone,
                    Category = 3,
                    Description = "Test Work Item " + i
                };
                invokeTasks.Add(host.CallAsync(method, new { workItem = workItem }));
            }
            await Task.WhenAll(invokeTasks.ToArray());

            Assert.False(TestJobs.FailureDetected);
            Assert.Equal(numInvocations, TestJobs.JobInvocations[1]);

            VerifyLeaseState(method, "TestValue", LeaseState.Available, LeaseStatus.Unlocked);

            host.Stop();
            host.Dispose();
        }

        [Fact]
        public async Task SingletonListener_MultipleHosts_OnlyOneHostRunsListener()
        {
            // create and start multiple hosts concurrently
            int numHosts = 3;
            List<JobHost> hosts = new List<JobHost>();
            Task[] tasks = new Task[numHosts];
            for (int i = 0; i < numHosts; i++)
            {
                JobHost host = CreateTestJobHost(i);
                hosts.Add(host);
                tasks[i] = host.StartAsync();
            }
            await Task.WhenAll(tasks);

            // verify that only 2 listeners were started (one for each of the singleton functions)
            Assert.Equal(3, TestTriggerAttributeBindingProvider.TestTriggerBinding.TestTriggerListener.StartCount);

            MethodInfo singletonListenerMethod = typeof(TestJobs).GetMethod("TriggerJob_SingletonListener");
            VerifyLeaseState(singletonListenerMethod, "Listener", LeaseState.Leased, LeaseStatus.Locked);

            MethodInfo singletonListenerAndFunctionMethod = typeof(TestJobs).GetMethod("SingletonTriggerJob_SingletonListener");
            VerifyLeaseState(singletonListenerAndFunctionMethod, "Listener", LeaseState.Leased, LeaseStatus.Locked);

            // stop all the hosts
            foreach (JobHost host in hosts)
            {
                await host.StopAsync();
                host.Dispose();
            }

            VerifyLeaseState(singletonListenerMethod, "Listener", LeaseState.Available, LeaseStatus.Unlocked);
            VerifyLeaseState(singletonListenerAndFunctionMethod, "Listener", LeaseState.Available, LeaseStatus.Unlocked);
        }

        [Fact]
        public async Task SingletonListener_SingletonFunction_InvocationsAreSerialized()
        {
            JobHost host = CreateTestJobHost(1);
            await host.StartAsync();

            MethodInfo singletonListenerAndFunctionMethod = typeof(TestJobs).GetMethod("SingletonTriggerJob_SingletonListener");
            VerifyLeaseState(singletonListenerAndFunctionMethod, "Listener", LeaseState.Leased, LeaseStatus.Locked);

            await host.CallAsync(singletonListenerAndFunctionMethod, new { test = "Test" });

            await host.StopAsync();
            host.Dispose();

            VerifyLeaseState(singletonListenerAndFunctionMethod, "TestScopeTestValue", LeaseState.Available, LeaseStatus.Unlocked);
            VerifyLeaseState(singletonListenerAndFunctionMethod, "Listener", LeaseState.Available, LeaseStatus.Unlocked);
        }

        [Fact]
        public async Task SingletonListener_SingletonFunction_ListenerSingletonOverride_InvocationsAreSerialized()
        {
            JobHost host = CreateTestJobHost(1);
            await host.StartAsync();

            MethodInfo singletonListenerAndFunctionMethod = typeof(TestJobs).GetMethod("SingletonTriggerJob_SingletonListener_ListenerSingletonOverride");
            VerifyLeaseState(singletonListenerAndFunctionMethod, "TestScopeTestValue.Listener", LeaseState.Leased, LeaseStatus.Locked);

            await host.CallAsync(singletonListenerAndFunctionMethod, new { test = "Test" });

            await host.StopAsync();
            host.Dispose();

            VerifyLeaseState(singletonListenerAndFunctionMethod, "TestScope", LeaseState.Available, LeaseStatus.Unlocked);
            VerifyLeaseState(singletonListenerAndFunctionMethod, "TestScopeTestValue.Listener", LeaseState.Available, LeaseStatus.Unlocked);
        }

        [Fact]
        public async Task SingletonTriggerFunction_MultipleConcurrentInvocations_InvocationsAreSerialized()
        {
            JobHost host = CreateTestJobHost(1);
            host.Start();

            // trigger a bunch of parallel invocations
            int numMessages = 20;
            List<Task> invokeTasks = new List<Task>();
            JsonSerializer serializer = new JsonSerializer();
            for (int i = 0; i < numMessages; i++)
            {
                int zone = _rand.Next(3) + 1;

                JObject workItem = new JObject
                {
                    { "ID", i + 1 },
                    { "Region", "Central" },
                    { "Zone", zone },
                    { "Category", 3 },
                    { "Description", "Test Work Item " + i }
                };
                await host.CallAsync(typeof(TestJobs).GetMethod("EnqueueQueue2TestMessage"), new { message = workItem.ToString() });
            }

            // wait for all the messages to be processed by the job
            await TestHelpers.Await(() =>
            {
                return (TestJobs.Queue2MessageCount == numMessages &&
                       TestJobs.JobInvocations.Select(p => p.Value).Sum() == numMessages) || TestJobs.FailureDetected;
            }, pollingInterval: 500);

            Assert.False(TestJobs.FailureDetected);
            Assert.Equal(numMessages, TestJobs.JobInvocations[1]);

            VerifyLeaseState(typeof(TestJobs).GetMethod("SingletonTriggerJob"), "Central/1", LeaseState.Available, LeaseStatus.Unlocked);
            VerifyLeaseState(typeof(TestJobs).GetMethod("SingletonTriggerJob"), "Central/2", LeaseState.Available, LeaseStatus.Unlocked);
            VerifyLeaseState(typeof(TestJobs).GetMethod("SingletonTriggerJob"), "Central/3", LeaseState.Available, LeaseStatus.Unlocked);

            host.Stop();
            host.Dispose();
        }

        [Fact]
        public async Task SingletonFunction_Exception_LeaseReleasedImmediately()
        {
            JobHost host = CreateTestJobHost(1);
            host.Start();

            WorkItem workItem = new WorkItem
            {
                ID = 1,
                Region = "Central",
                Zone = 3,
                Category = -1,
                Description = "Test Work Item"
            };

            Exception exception = null;
            MethodInfo method = typeof(TestJobs).GetMethod("SingletonJob");
            try
            {
                await host.CallAsync(method, new { workItem = workItem });
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            Assert.Equal("Exception while executing function: TestJobs.SingletonJob", exception.Message);
            VerifyLeaseState(method, "TestValue", LeaseState.Available, LeaseStatus.Unlocked);

            host.Stop();
            host.Dispose();
        }

        [Fact]
        public async Task QueueFunction_SingletonListener()
        {
            JobHost host = CreateTestJobHost(1);
            await host.StartAsync();

            MethodInfo method = typeof(TestJobs).GetMethod("QueueFunction_SingletonListener");
            VerifyLeaseState(method, "Listener", LeaseState.Leased, LeaseStatus.Locked);

            await host.CallAsync(method, new { message = "{}" });

            await host.StopAsync();
            host.Dispose();

            VerifyLeaseState(method, "Listener", LeaseState.Available, LeaseStatus.Unlocked);
        }

        [Fact]
        public async Task SingletonFunction_StorageAccountOverride()
        {
            JobHost host = CreateTestJobHost(1);
            await host.StartAsync();

            MethodInfo method = typeof(TestJobs).GetMethod("SingletonJob_StorageAccountOverride");

            await host.CallAsync(method, new { message = "{}" });

            await host.StopAsync();
            host.Dispose();

            // make sure the lease blob was only created in the secondary account
            VerifyLeaseDoesNotExist(method, null);
            VerifyLeaseState(method, null, LeaseState.Available, LeaseStatus.Unlocked, directory: _secondaryLockDirectory);
        }

        internal static void VerifyLeaseState(MethodInfo method, string scope, LeaseState leaseState, LeaseStatus leaseStatus, CloudBlobDirectory directory = null)
        {
            string lockId = FormatLockId(method, scope);

            CloudBlobDirectory lockDirectory = directory ?? _lockDirectory;
            CloudBlockBlob lockBlob = lockDirectory.GetBlockBlobReference(lockId);
            lockBlob.FetchAttributes();
            Assert.Equal(leaseState, lockBlob.Properties.LeaseState);
            Assert.Equal(leaseStatus, lockBlob.Properties.LeaseStatus);
        }

        internal static void VerifyLeaseDoesNotExist(MethodInfo method, string scope, CloudBlobDirectory directory = null)
        {
            string lockId = FormatLockId(method, scope);

            CloudBlobDirectory lockDirectory = directory ?? _lockDirectory;
            CloudBlockBlob lockBlob = lockDirectory.GetBlockBlobReference(lockId);
            Assert.False(lockBlob.Exists());
        }

        private static string FormatLockId(MethodInfo method, string scope)
        {
            string lockId = string.Format("{0}.{1}", method.DeclaringType.FullName, method.Name);
            if (!string.IsNullOrEmpty(scope))
            {
                lockId += "." + scope;
            }
            return lockId;
        }

        public class WorkItem
        {
            public int ID { get; set; }
            public string Region { get; set; }
            public int Zone { get; set; }
            public int Category { get; set; }
            public string Description { get; set; }
        }

        public class TestJobs
        {
            private const string Secondary = "SecondaryStorage";
            public const string LeaseBlobRootPath = "Microsoft.Azure.WebJobs.Host.EndToEndTests.SingletonEndToEndTests+TestJobs";
            public static int Queue1MessageCount = 0;
            public static int Queue2MessageCount = 0;
            public static bool FailureDetected = false;
            public static Dictionary<int, int> JobInvocations = new Dictionary<int, int>();
            private static Dictionary<string, bool> ScopeLocks = new Dictionary<string, bool>();
            private static object syncLock = new object();

            private readonly int _hostId;

            public TestJobs(int hostId)
            {
                _hostId = hostId;
            }

            [Singleton(@"{Region}\{Zone}")]
            public async Task SingletonTriggerJob([QueueTrigger(Queue2Name)] WorkItem workItem)
            {
                VerifyLeaseState(
                    GetType().GetMethod("SingletonTriggerJob"), 
                    string.Format("{0}/{1}", workItem.Region, workItem.Zone), 
                    LeaseState.Leased, LeaseStatus.Locked);

                // When run concurrently, this job will fail very reliably
                string scope = workItem.Region + workItem.Zone.ToString();
                UpdateScopeLock(scope, true);

                await Task.Delay(50);
                IncrementJobInvocationCount();

                UpdateScopeLock(scope, false);
            }

            [Singleton("%test%")]
            [NoAutomaticTrigger]
            public async Task SingletonJob(WorkItem workItem)
            {
                VerifyLeaseState(
                    GetType().GetMethod("SingletonJob"), 
                    "TestValue", 
                    LeaseState.Leased, LeaseStatus.Locked);

                if (workItem.Category < 0)
                {
                    throw new Exception("Kaboom!");
                }

                // When run concurrently, this job will fail very reliably
                string scope = "default";
                UpdateScopeLock(scope, true);

                await Task.Delay(50);
                IncrementJobInvocationCount();

                UpdateScopeLock(scope, false);
            }

            [Singleton(Account = Secondary)]
            [NoAutomaticTrigger]
            public async Task SingletonJob_StorageAccountOverride()
            {
                VerifyLeaseState(
                    GetType().GetMethod("SingletonJob_StorageAccountOverride"),
                    null,
                    LeaseState.Leased, LeaseStatus.Locked,
                    _secondaryLockDirectory);

                await Task.Delay(50);
                IncrementJobInvocationCount();
            }

            // Job with an implicit Singleton lock on the trigger listener
            public async Task TriggerJob_SingletonListener([TestTrigger] string test)
            {
                VerifyLeaseState(
                    GetType().GetMethod("TriggerJob_SingletonListener"), 
                    "Listener", 
                    LeaseState.Leased, LeaseStatus.Locked);

                await Task.Delay(50);
                IncrementJobInvocationCount();
            }

            // Job with BOTH an implicit Singleton lock on the trigger listener as
            // well as a explicit function Singleton. This means that there will only
            // be a single listener running, and also means that individual invocations
            // are also serialized by scope.
            [Singleton("TestScope%test%")]
            public async Task SingletonTriggerJob_SingletonListener([TestTrigger] string test)
            {
                VerifyLeaseState(
                    GetType().GetMethod("SingletonTriggerJob_SingletonListener"),
                    "TestScopeTestValue",
                    LeaseState.Leased, LeaseStatus.Locked);

                VerifyLeaseState(
                    GetType().GetMethod("SingletonTriggerJob_SingletonListener"),
                    "Listener",
                    LeaseState.Leased, LeaseStatus.Locked);

                await Task.Delay(50);
                IncrementJobInvocationCount();
            }

            // Override the implicit listener Singleton by providing our own
            // Singleton using Mode = Listener.
            [Singleton("TestScope")]
            [Singleton("TestScope%test%", Mode = SingletonMode.Listener)]
            public async Task SingletonTriggerJob_SingletonListener_ListenerSingletonOverride([TestTrigger] string test)
            {
                VerifyLeaseState(
                    GetType().GetMethod("SingletonTriggerJob_SingletonListener_ListenerSingletonOverride"),
                    "TestScope",
                    LeaseState.Leased, LeaseStatus.Locked);

                VerifyLeaseState(
                    GetType().GetMethod("SingletonTriggerJob_SingletonListener_ListenerSingletonOverride"),
                    "TestScopeTestValue.Listener",
                    LeaseState.Leased, LeaseStatus.Locked);

                await Task.Delay(50);
                IncrementJobInvocationCount();
            }

            [Singleton(Mode = SingletonMode.Listener)]
            public async Task QueueFunction_SingletonListener([QueueTrigger("xyz123")] string message)
            {
                VerifyLeaseState(
                    GetType().GetMethod("QueueFunction_SingletonListener"),
                    "Listener",
                    LeaseState.Leased, LeaseStatus.Locked);

                await Task.Delay(50);
                IncrementJobInvocationCount();
            }

            [NoAutomaticTrigger]
            public void EnqueueQueue1TestMessage(string message,
                [Queue(Queue1Name)] ICollector<string> queueMessages)
            {
                queueMessages.Add(message);
                Interlocked.Increment(ref Queue1MessageCount);
            }

            [NoAutomaticTrigger]
            public void EnqueueQueue2TestMessage(string message,
                [Queue(Queue2Name)] ICollector<string> queueMessages)
            {
                queueMessages.Add(message);
                Interlocked.Increment(ref Queue2MessageCount);
            }

            public static void Reset()
            {
                Queue1MessageCount = 0;
                Queue2MessageCount = 0;
                JobInvocations = new Dictionary<int, int>();
                ScopeLocks = new Dictionary<string, bool>();
                FailureDetected = false;
            }

            private void IncrementJobInvocationCount()
            {
                lock (syncLock)
                {
                    if (!JobInvocations.ContainsKey(_hostId))
                    {
                        JobInvocations[_hostId] = 0;
                    }
                    JobInvocations[_hostId]++;
                }
            }

            private static void UpdateScopeLock(string scope, bool isLocked)
            {
                bool scopeIsLocked = false;
                if (ScopeLocks.TryGetValue(scope, out scopeIsLocked) 
                    && scopeIsLocked && isLocked)
                {
                    FailureDetected = true;
                }
                ScopeLocks[scope] = isLocked;
            }
        }

        private JobHost CreateTestJobHost(int hostId)
        {
            TestJobActivator activator = new TestJobActivator(hostId);

            JobHostConfiguration config = new JobHostConfiguration
            {
                NameResolver = _resolver,
                TypeLocator = new FakeTypeLocator(typeof(TestJobs)),
                JobActivator = activator
            };
            config.Queues.MaxPollingInterval = TimeSpan.FromSeconds(2);
            config.Singleton.LockAcquisitionTimeout = TimeSpan.FromSeconds(10);
            config.Singleton.LockAcquisitionPollingInterval = TimeSpan.FromMilliseconds(500);

            IExtensionRegistry registry = config.GetService<IExtensionRegistry>();
            registry.RegisterExtension<ITriggerBindingProvider>(new TestTriggerAttributeBindingProvider());

            JobHost host = new JobHost(config);

            return host;
        }

        private class TestJobActivator : IJobActivator
        {
            private int _hostId;

            public TestJobActivator(int hostId)
            {
                _hostId = hostId;
            }

            public T CreateInstance<T>()
            {
                return (T)Activator.CreateInstance(typeof(T), _hostId);
            }
        }

        [AttributeUsage(AttributeTargets.Parameter)]
        public class TestTriggerAttribute : Attribute
        {
            public TestTriggerAttribute()
            {
            }
        }

        /// <summary>
        /// Test trigger binding that applies SingletonAttribute to its listener implementation.
        /// </summary>
        public class TestTriggerAttributeBindingProvider : ITriggerBindingProvider
        {
            public Task<ITriggerBinding> TryCreateAsync(TriggerBindingProviderContext context)
            {
                TestTriggerAttribute attribute = context.Parameter.GetCustomAttributes<TestTriggerAttribute>().SingleOrDefault();
                ITriggerBinding binding = null;

                if (attribute != null)
                {
                    binding = new TestTriggerBinding();
                }

                return Task.FromResult(binding);
            }

            public class TestTriggerBinding : ITriggerBinding
            {
                public Type TriggerValueType
                {
                    get { return typeof(string); }
                }

                public IReadOnlyDictionary<string, Type> BindingDataContract
                {
                    get { return new Dictionary<string, Type>(); }
                }

                public Task<ITriggerData> BindAsync(object value, ValueBindingContext context)
                {
                    return Task.FromResult<ITriggerData>(new TestTriggerData());
                }

                public Task<IListener> CreateListenerAsync(ListenerFactoryContext context)
                {
                    return Task.FromResult<IListener>(new TestTriggerListener());
                }

                public ParameterDescriptor ToParameterDescriptor()
                {
                    return new ParameterDescriptor();
                }

                [Singleton(Mode = SingletonMode.Listener)]
                public class TestTriggerListener : IListener
                {
                    public static int StartCount = 0;

                    public Task StartAsync(CancellationToken cancellationToken)
                    {
                        Interlocked.Increment(ref StartCount);
                        return Task.FromResult(true);
                    }

                    public Task StopAsync(CancellationToken cancellationToken)
                    {
                        return Task.FromResult(true);
                    }

                    public void Cancel()
                    {
                    }

                    public void Dispose()
                    {
                    }
                }

                private class TestTriggerData : ITriggerData
                {
                    public IValueProvider ValueProvider
                    {
                        get { return new TestValueProvider(); }
                    }

                    public IReadOnlyDictionary<string, object> BindingData
                    {
                        get { return new Dictionary<string, object>(); }
                    }

                    private class TestValueProvider : IValueProvider
                    {
                        public Type Type
                        {
                            get { return typeof(string); }
                        }

                        public object GetValue()
                        {
                            return "Test";
                        }

                        public string ToInvokeString()
                        {
                            return "Test";
                        }
                    }
                }
            }
        }

        private class TestNameResolver : RandomNameResolver
        {
            public override string Resolve(string name)
            {
                if (name == "test")
                {
                    return "TestValue";
                }
                return base.Resolve(name);
            }
        }

        private class TestFixture : IDisposable
        {
            private CloudStorageAccount storageAccount;

            public TestFixture()
            {
                JobHostConfiguration config = new JobHostConfiguration();
                storageAccount = CloudStorageAccount.Parse(config.StorageConnectionString);
            }

            public void Dispose()
            {
                if (storageAccount != null)
                {
                    CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
                    foreach (var queue in queueClient.ListQueues(TestArtifactsPrefix))
                    {
                        queue.Delete();
                    }
                }
            }
        }
    }


}
