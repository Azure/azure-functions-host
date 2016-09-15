// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.EndToEndTests
{
    public class AsyncChainEndToEndTests : IClassFixture<AsyncChainEndToEndTests.TestFixture>
    {
        private const string TestArtifactsPrefix = "asynce2e";

        private const string ContainerName = TestArtifactsPrefix + "%rnd%";

        private const string NonWebJobsBlobName = "NonWebJobs";
        private const string Blob1Name = "Blob1";
        private const string Blob2Name = "Blob2";

        private const string Queue1Name = TestArtifactsPrefix + "q1%rnd%";
        private const string Queue2Name = TestArtifactsPrefix + "q2%rnd%";
        private const string TestQueueName = TestArtifactsPrefix + "q3%rnd%";

        private static CloudStorageAccount _storageAccount;

        private static RandomNameResolver _resolver;
        private static JobHostConfiguration _hostConfig;

        private static EventWaitHandle _functionCompletedEvent;

        private static string _finalBlobContent;
        private static TimeSpan _timeoutJobDelay;

        private readonly CloudQueue _testQueue;
        private readonly TestFixture _fixture;

        public AsyncChainEndToEndTests(TestFixture fixture)
        {
            _fixture = fixture;
            _resolver = new RandomNameResolver();
            _hostConfig = new JobHostConfiguration()
            {
                NameResolver = _resolver,
                TypeLocator = new FakeTypeLocator(typeof(AsyncChainEndToEndTests))
            };

            _hostConfig.Queues.MaxPollingInterval = TimeSpan.FromSeconds(2);

            _storageAccount = fixture.StorageAccount;
            _timeoutJobDelay = TimeSpan.FromMinutes(5);

            CloudQueueClient queueClient = _storageAccount.CreateCloudQueueClient();
            string queueName = _resolver.ResolveInString(TestQueueName);
            _testQueue = queueClient.GetQueueReference(queueName);
            if (!_testQueue.CreateIfNotExists())
            {
                _testQueue.Clear();
            }
        }

        [Fact]
        public async Task AsyncChainEndToEnd()
        {
            using (_functionCompletedEvent = new ManualResetEvent(initialState: false))
            {
                TextWriter hold = Console.Out;
                StringWriter consoleOutput = new StringWriter();
                Console.SetOut(consoleOutput);

                await AsyncChainEndToEndInternal();

                string firstQueueName = _resolver.ResolveInString(Queue1Name);
                string secondQueueName = _resolver.ResolveInString(Queue2Name);
                string blobContainerName = _resolver.ResolveInString(ContainerName);
                string[] consoleOutputLines = consoleOutput.ToString().Trim().Split(new string[] { Environment.NewLine }, StringSplitOptions.None).OrderBy(p => p).ToArray();
                string[] expectedOutputLines = new string[]
                {
                    "Found the following functions:",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.AsyncChainEndToEndTests.WriteStartDataMessageToQueue",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.AsyncChainEndToEndTests.QueueToQueueAsync",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.AsyncChainEndToEndTests.QueueToBlobAsync",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.AsyncChainEndToEndTests.AlwaysFailJob",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.AsyncChainEndToEndTests.DisabledJob",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.AsyncChainEndToEndTests.QueueTrigger_TraceLevelOverride",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.AsyncChainEndToEndTests.TimeoutJob",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.AsyncChainEndToEndTests.TimeoutJob_Throw",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.AsyncChainEndToEndTests.TimeoutJob_Throw_NoToken",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.AsyncChainEndToEndTests.BlobToBlobAsync",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.AsyncChainEndToEndTests.ReadResultBlob",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.AsyncChainEndToEndTests.RandGuidOutput",
                    "Function 'AsyncChainEndToEndTests.DisabledJob' is disabled",
                    "Job host started",
                    "Executing: 'AsyncChainEndToEndTests.WriteStartDataMessageToQueue' - Reason: 'This function was programmatically called via the host APIs.'",
                    "Executed: 'AsyncChainEndToEndTests.WriteStartDataMessageToQueue' (Succeeded)",
                    string.Format("Executing: 'AsyncChainEndToEndTests.QueueToQueueAsync' - Reason: 'New queue message detected on '{0}'.'", firstQueueName),
                    "Executed: 'AsyncChainEndToEndTests.QueueToQueueAsync' (Succeeded)",
                    string.Format("Executing: 'AsyncChainEndToEndTests.QueueToBlobAsync' - Reason: 'New queue message detected on '{0}'.'", secondQueueName),
                    "Executed: 'AsyncChainEndToEndTests.QueueToBlobAsync' (Succeeded)",
                    string.Format("Executing: 'AsyncChainEndToEndTests.BlobToBlobAsync' - Reason: 'New blob detected: {0}/Blob1'", blobContainerName),
                    "Executed: 'AsyncChainEndToEndTests.BlobToBlobAsync' (Succeeded)",
                    "Job host stopped",
                    "Executing: 'AsyncChainEndToEndTests.ReadResultBlob' - Reason: 'This function was programmatically called via the host APIs.'",
                    "Executed: 'AsyncChainEndToEndTests.ReadResultBlob' (Succeeded)"
                }.OrderBy(p => p).ToArray();

                bool hasError = consoleOutputLines.Any(p => p.Contains("Function had errors"));
                if (!hasError)
                {
                    Assert.Equal(
                    string.Join(Environment.NewLine, expectedOutputLines),
                    string.Join(Environment.NewLine, consoleOutputLines)
                    );
                }

                Console.SetOut(hold);
            }
        }

        [Fact]
        public async Task AsyncChainEndToEnd_CustomFactories()
        {
            using (_functionCompletedEvent = new ManualResetEvent(initialState: false))
            {
                CustomQueueProcessorFactory queueProcessorFactory = new CustomQueueProcessorFactory();
                _hostConfig.Queues.QueueProcessorFactory = queueProcessorFactory;

                CustomStorageClientFactory storageClientFactory = new CustomStorageClientFactory();
                _hostConfig.StorageClientFactory = storageClientFactory;

                await AsyncChainEndToEndInternal();

                Assert.Equal(3, queueProcessorFactory.CustomQueueProcessors.Count);
                Assert.True(queueProcessorFactory.CustomQueueProcessors.All(p => p.Context.Queue.Name.StartsWith("asynce2eq")));
                Assert.True(queueProcessorFactory.CustomQueueProcessors.Sum(p => p.BeginProcessingCount) >= 2);
                Assert.True(queueProcessorFactory.CustomQueueProcessors.Sum(p => p.CompleteProcessingCount) >= 2);

                Assert.Equal(16, storageClientFactory.TotalBlobClientCount);
                Assert.Equal(11, storageClientFactory.TotalQueueClientCount);
                Assert.Equal(0, storageClientFactory.TotalTableClientCount);

                Assert.Equal(6, storageClientFactory.ParameterBlobClientCount);
                Assert.Equal(7, storageClientFactory.ParameterQueueClientCount);
                Assert.Equal(0, storageClientFactory.ParameterTableClientCount);
            }
        }

        [Fact]
        public async Task TraceWriterLogging()
        {
            TextWriter hold = Console.Out;
            StringWriter consoleOutput = new StringWriter();
            Console.SetOut(consoleOutput);

            using (_functionCompletedEvent = new ManualResetEvent(initialState: false))
            {
                TestTraceWriter trace = new TestTraceWriter(TraceLevel.Verbose);
                _hostConfig.Tracing.Tracers.Add(trace);
                JobHost host = new JobHost(_hostConfig);

                await host.StartAsync();
                await host.CallAsync(typeof(AsyncChainEndToEndTests).GetMethod("WriteStartDataMessageToQueue"));

                _functionCompletedEvent.WaitOne();

                // ensure all logs have had a chance to flush
                await Task.Delay(3000);

                await host.StopAsync();

                bool hasError = string.Join(Environment.NewLine, trace.Traces.Where(p => p.Message.Contains("Error"))).Any();
                if (!hasError)
                {
                    Assert.Equal(18, trace.Traces.Count);
                    Assert.NotNull(trace.Traces.SingleOrDefault(p => p.Message.Contains("User TraceWriter log")));
                    Assert.NotNull(trace.Traces.SingleOrDefault(p => p.Message.Contains("User TextWriter log (TestParam)")));
                    Assert.NotNull(trace.Traces.SingleOrDefault(p => p.Message.Contains("Another User TextWriter log")));

                    string[] consoleOutputLines = consoleOutput.ToString().Trim().Split(new string[] { Environment.NewLine }, StringSplitOptions.None);
                    Assert.Equal(24, consoleOutputLines.Length);
                    Assert.Null(consoleOutputLines.SingleOrDefault(p => p.Contains("User TraceWriter log")));
                    Assert.Null(consoleOutputLines.SingleOrDefault(p => p.Contains("User TextWriter log (TestParam)")));
                }
            }

            Console.SetOut(hold);
        }

        [Fact]
        public void FunctionFailures_LogsExpectedTraceEvent()
        {
            TestTraceWriter trace = new TestTraceWriter(TraceLevel.Verbose);
            _hostConfig.Tracing.Tracers.Add(trace);
            JobHost host = new JobHost(_hostConfig);

            MethodInfo methodInfo = GetType().GetMethod("AlwaysFailJob");
            try
            {
                host.Call(methodInfo);
            }
            catch { }

            // We expect 3 error messages total
            TraceEvent[] traceErrors = trace.Traces.Where(p => p.Level == TraceLevel.Error).ToArray();
            Assert.Equal(3, traceErrors.Length);

            // Ensure that all errors include the same exception, with function
            // invocation details
            FunctionInvocationException functionException = traceErrors.First().Exception as FunctionInvocationException;
            Assert.NotNull(functionException);
            Assert.NotEqual(Guid.Empty, functionException.InstanceId);
            Assert.Equal(string.Format("{0}.{1}", methodInfo.DeclaringType.FullName, methodInfo.Name), functionException.MethodName);
            Assert.True(traceErrors.All(p => functionException == p.Exception));
        }

        [Fact]
        public void RandGuidOutput_GeneratesRandomIDs()
        {
            JobHost host = new JobHost(_hostConfig);

            var blobClient = _fixture.StorageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference("test-output");
            if (container.Exists())
            {
                foreach (CloudBlockBlob blob in container.ListBlobs())
                {
                    blob.Delete();
                }
            }

            MethodInfo methodInfo = GetType().GetMethod("RandGuidOutput");
            for (int i = 0; i < 3; i++)
            {
                var arguments = new Dictionary<string, object>
                {
                    { "input", i.ToString() }
                };
                host.Call(methodInfo, arguments);
            }

            // We expect 3 separate blobs to have been written
            var blobs = container.ListBlobs().Cast<CloudBlockBlob>().ToArray();
            Assert.Equal(3, blobs.Length);
            foreach (var blob in blobs)
            {
                string content = blob.DownloadText(Encoding.UTF8);
                int blobInt = int.Parse(content.Trim(new char[] { '\uFEFF', '\u200B' }));
                Assert.True(blobInt >= 0 && blobInt <= 3);
            }
        }

        [Fact]
        public async Task Timeout_TimeoutExpires_Cancels()
        {
            var exceptionHandler = new TestExceptionHandler();
            await RunTimeoutTest(exceptionHandler, typeof(TaskCanceledException), "TimeoutJob");
            Assert.Empty(exceptionHandler.UnhandledExceptionInfos);
            Assert.Empty(exceptionHandler.TimeoutExceptionInfos);
        }

        [Fact]
        public async Task TimeoutWithThrow_TimeoutExpires_CancelsAndThrows()
        {
            var exceptionHandler = new TestExceptionHandler();
            await RunTimeoutTest(exceptionHandler, typeof(FunctionTimeoutException), "TimeoutJob_Throw");
            var exception = exceptionHandler.TimeoutExceptionInfos.Single().SourceException;
            Assert.IsType<FunctionTimeoutException>(exception);
            Assert.Empty(exceptionHandler.UnhandledExceptionInfos);
        }

        [Fact]
        public async Task TimeoutWithThrow_NoCancellationToken_CancelsAndThrows()
        {
            var exceptionHandler = new TestExceptionHandler();
            await RunTimeoutTest(exceptionHandler, typeof(FunctionTimeoutException), "TimeoutJob_Throw_NoToken");
            var exception = exceptionHandler.TimeoutExceptionInfos.Single().SourceException;
            Assert.IsType<FunctionTimeoutException>(exception);
            Assert.Empty(exceptionHandler.UnhandledExceptionInfos);
        }

        private async Task RunTimeoutTest(IWebJobsExceptionHandler exceptionHandler, Type expectedExceptionType, string functionName)
        {
            TestTraceWriter trace = new TestTraceWriter(TraceLevel.Verbose);
            _hostConfig.Tracing.Tracers.Add(trace);
            _hostConfig.AddService<IWebJobsExceptionHandler>(exceptionHandler);
            JobHost host = new JobHost(_hostConfig);

            try
            {
                await host.StartAsync();

                MethodInfo methodInfo = GetType().GetMethod(functionName);
                Exception ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
                {
                    await host.CallAsync(methodInfo);
                });

                Assert.IsType(expectedExceptionType, ex);
            }
            finally
            {
                host.Stop();
            }

            // We expect 3 error messages total
            TraceEvent[] traceErrors = trace.Traces.Where(p => p.Level == TraceLevel.Error).ToArray();
            Assert.Equal(3, traceErrors.Length);
            Assert.True(traceErrors[0].Message.StartsWith(string.Format("Timeout value of 00:00:01 exceeded by function 'AsyncChainEndToEndTests.{0}'", functionName)));
            Assert.True(traceErrors[1].Message.StartsWith(string.Format("Executed: 'AsyncChainEndToEndTests.{0}' (Failed)", functionName)));
            Assert.True(traceErrors[2].Message.Trim().StartsWith("Function had errors. See Azure WebJobs SDK dashboard for details."));
        }

        [Fact]
        public async Task Timeout_NoExpiry_CompletesSuccessfully()
        {
            TestTraceWriter trace = new TestTraceWriter(TraceLevel.Verbose);
            _hostConfig.Tracing.Tracers.Add(trace);
            JobHost host = new JobHost(_hostConfig);

            _timeoutJobDelay = TimeSpan.FromSeconds(0);
            MethodInfo methodInfo = GetType().GetMethod("TimeoutJob");
            await host.CallAsync(methodInfo);

            TraceEvent[] traceErrors = trace.Traces.Where(p => p.Level == TraceLevel.Error).ToArray();
            Assert.Equal(0, traceErrors.Length);
        }

        [Fact]
        public async Task FunctionTraceLevelOverride_ProducesExpectedOutput()
        {
            TestTraceWriter trace = new TestTraceWriter(TraceLevel.Verbose);
            _hostConfig.Tracing.Tracers.Add(trace);
            JobHost host = new JobHost(_hostConfig);

            try
            {
                using (_functionCompletedEvent = new ManualResetEvent(initialState: false))
                {
                    await host.StartAsync();

                    CloudQueueMessage message = new CloudQueueMessage("test message");
                    _testQueue.AddMessage(message);

                    _functionCompletedEvent.WaitOne();

                    // wait for logs to flush
                    await Task.Delay(3000);

                    // expect no function output
                    TraceEvent[] traces = trace.Traces.ToArray();
                    Assert.Equal(5, traces.Length);
                    Assert.False(traces.Any(p => p.Message.Contains("test message")));
                }
            }
            finally
            {
                host.Stop();
            }
        }

        [Fact]
        public async Task FunctionTraceLevelOverride_Failure_ProducesExpectedOutput()
        {
            TestTraceWriter trace = new TestTraceWriter(TraceLevel.Verbose);
            _hostConfig.Tracing.Tracers.Add(trace);
            _hostConfig.Queues.MaxDequeueCount = 1;
            JobHost host = new JobHost(_hostConfig);

            try
            {
                using (_functionCompletedEvent = new ManualResetEvent(initialState: false))
                {
                    await host.StartAsync();

                    CloudQueueMessage message = new CloudQueueMessage("throw_message");
                    _testQueue.AddMessage(message);

                    _functionCompletedEvent.WaitOne();

                    // wait for logs to flush
                    await Task.Delay(3000);

                    // expect normal logs to be written (TraceLevel override is ignored)
                    TraceEvent[] traces = trace.Traces.ToArray();
                    Assert.Equal(10, traces.Length);

                    string output = string.Join("\r\n", traces.Select(p => p.Message));
                    Assert.True(output.Contains("Executing: 'AsyncChainEndToEndTests.QueueTrigger_TraceLevelOverride'"));
                    Assert.True(output.Contains("Exception while executing function: AsyncChainEndToEndTests.QueueTrigger_TraceLevelOverride"));
                    Assert.True(output.Contains("Executed: 'AsyncChainEndToEndTests.QueueTrigger_TraceLevelOverride' (Failed)"));
                    Assert.True(output.Contains("Message has reached MaxDequeueCount of 1"));
                }
            }
            finally
            {
                host.Stop();
            }
        }

        [NoAutomaticTrigger]
        public static async Task WriteStartDataMessageToQueue(
            [Queue(Queue1Name)] ICollector<string> queueMessages,
            [Blob(ContainerName + "/" + NonWebJobsBlobName, FileAccess.Write)] Stream nonSdkBlob,
            CancellationToken token)
        {
            queueMessages.Add(" works");

            byte[] messageBytes = Encoding.UTF8.GetBytes("async");
            await nonSdkBlob.WriteAsync(messageBytes, 0, messageBytes.Length);
        }

        [NoAutomaticTrigger]
        public static void AlwaysFailJob()
        {
            throw new Exception("Kaboom!");
        }

        [NoAutomaticTrigger]
        public static void RandGuidOutput(
            [QueueTrigger("test")] string input,
            [Blob("test-output/{rand-guid}")] out string blob)
        {
            blob = input;
        }

        [Disable("Disable_DisabledJob")]
        public static void DisabledJob([QueueTrigger(Queue1Name)] string message)
        {
        }

        [NoAutomaticTrigger]
        [Timeout("00:00:01", TimeoutWhileDebugging = true)]
        public static async Task TimeoutJob(CancellationToken cancellationToken, TextWriter log)
        {
            log.WriteLine("Started");
            await Task.Delay(_timeoutJobDelay, cancellationToken);
            log.WriteLine("Completed");
        }

        [NoAutomaticTrigger]
        [Timeout("00:00:01", ThrowOnTimeout = true, TimeoutWhileDebugging = true)]
        public static async Task TimeoutJob_Throw(CancellationToken cancellationToken, TextWriter log)
        {
            log.WriteLine("Started");
            await Task.Delay(_timeoutJobDelay, cancellationToken);
            log.WriteLine("Completed");
        }

        [NoAutomaticTrigger]
        [Timeout("00:00:01", ThrowOnTimeout = true, TimeoutWhileDebugging = true)]
        public static async Task TimeoutJob_Throw_NoToken(TextWriter log)
        {
            log.WriteLine("Started");
            await Task.Delay(_timeoutJobDelay);
            log.WriteLine("Completed");
        }

        [TraceLevel(TraceLevel.Error)]
        public static void QueueTrigger_TraceLevelOverride(
            [QueueTrigger(TestQueueName)] string message, TextWriter log)
        {
            log.WriteLine(message);

            _functionCompletedEvent.Set();

            if (message == "throw_message")
            {
                throw new Exception("Kaboom!");
            }
        }

        public static async Task QueueToQueueAsync(
            [QueueTrigger(Queue1Name)] string message,
            [Queue(Queue2Name)] IAsyncCollector<string> output,
            CancellationToken token,
            TraceWriter trace)
        {
            CloudBlobClient blobClient = _storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference(_resolver.ResolveInString(ContainerName));
            CloudBlockBlob blob = container.GetBlockBlobReference(NonWebJobsBlobName);
            string blobContent = await blob.DownloadTextAsync();

            trace.Info("User TraceWriter log");

            await output.AddAsync(blobContent + message);
        }

        public static async Task QueueToBlobAsync(
            [QueueTrigger(Queue2Name)] string message,
            [Blob(ContainerName + "/" + Blob1Name, FileAccess.Write)] Stream blobStream,
            CancellationToken token,
            TextWriter log)
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);

            log.WriteLine("User TextWriter log ({0})", "TestParam");
            log.Write("Another User TextWriter log");

            await blobStream.WriteAsync(messageBytes, 0, messageBytes.Length);
        }

        public static async Task BlobToBlobAsync(
            [BlobTrigger(ContainerName + "/" + Blob1Name)] Stream inputStream,
            [Blob(ContainerName + "/" + Blob2Name, FileAccess.Write)] Stream outputStream,
            CancellationToken token)
        {
            // Should not be signaled
            if (token.IsCancellationRequested)
            {
                _functionCompletedEvent.Set();
                return;
            }

            await inputStream.CopyToAsync(outputStream);
            outputStream.Close();

            _functionCompletedEvent.Set();
        }

        public static void ReadResultBlob(
            [Blob(ContainerName + "/" + Blob2Name)] string blob,
            CancellationToken token)
        {
            // Should not be signaled
            if (token.IsCancellationRequested)
            {
                return;
            }

            _finalBlobContent = blob;
        }

        private async Task AsyncChainEndToEndInternal()
        {
            JobHost host = new JobHost(_hostConfig);

            Assert.Null(_hostConfig.HostId);

            await host.StartAsync();

            Assert.NotEmpty(_hostConfig.HostId);

            await host.CallAsync(typeof(AsyncChainEndToEndTests).GetMethod("WriteStartDataMessageToQueue"));

            _functionCompletedEvent.WaitOne();

            // ensure all logs have had a chance to flush
            await Task.Delay(3000);

            // Stop async waits for the function to complete
            await host.StopAsync();

            await host.CallAsync(typeof(AsyncChainEndToEndTests).GetMethod("ReadResultBlob"));
            Assert.Equal("async works", _finalBlobContent);
        }

        private class CustomQueueProcessorFactory : IQueueProcessorFactory
        {
            public List<CustomQueueProcessor> CustomQueueProcessors = new List<CustomQueueProcessor>();

            public QueueProcessor Create(QueueProcessorFactoryContext context)
            {
                // demonstrates how the Queue.ServiceClient options can be configured
                context.Queue.ServiceClient.DefaultRequestOptions.ServerTimeout = TimeSpan.FromSeconds(30);

                // demonstrates how queue options can be customized
                context.Queue.EncodeMessage = true;

                // demonstrates how batch processing behavior can be customized
                context.BatchSize = 30;
                context.NewBatchThreshold = 100;

                CustomQueueProcessor processor = new CustomQueueProcessor(context);
                CustomQueueProcessors.Add(processor);
                return processor;
            }
        }

        public class CustomQueueProcessor : QueueProcessor
        {
            public int BeginProcessingCount = 0;
            public int CompleteProcessingCount = 0;

            public CustomQueueProcessor(QueueProcessorFactoryContext context) : base(context)
            {
                Context = context;
            }

            public QueueProcessorFactoryContext Context { get; private set; }

            public override Task<bool> BeginProcessingMessageAsync(CloudQueueMessage message, CancellationToken cancellationToken)
            {
                BeginProcessingCount++;
                return base.BeginProcessingMessageAsync(message, cancellationToken);
            }

            public override Task CompleteProcessingMessageAsync(CloudQueueMessage message, FunctionResult result, CancellationToken cancellationToken)
            {
                CompleteProcessingCount++;
                return base.CompleteProcessingMessageAsync(message, result, cancellationToken);
            }

            protected override async Task ReleaseMessageAsync(CloudQueueMessage message, FunctionResult result, TimeSpan visibilityTimeout, CancellationToken cancellationToken)
            {
                // demonstrates how visibility timeout for failed messages can be customized
                // the logic here could implement exponential backoff, etc.
                visibilityTimeout = TimeSpan.FromSeconds(message.DequeueCount);

                await base.ReleaseMessageAsync(message, result, visibilityTimeout, cancellationToken);
            }
        }

        /// <summary>
        /// This custom <see cref="StorageClientFactory"/> demonstrates how clients can be customized.
        /// For example, users can configure global retry policies, DefaultRequestOptions, etc.
        /// </summary>
        public class CustomStorageClientFactory : StorageClientFactory
        {
            public int TotalBlobClientCount;
            public int TotalQueueClientCount;
            public int TotalTableClientCount;

            public int ParameterBlobClientCount;
            public int ParameterQueueClientCount;
            public int ParameterTableClientCount;

            public override CloudBlobClient CreateCloudBlobClient(StorageClientFactoryContext context)
            {
                TotalBlobClientCount++;

                if (context.Parameter != null)
                {
                    ParameterBlobClientCount++;
                }

                return base.CreateCloudBlobClient(context);
            }

            public override CloudQueueClient CreateCloudQueueClient(StorageClientFactoryContext context)
            {
                TotalQueueClientCount++;

                if (context.Parameter != null)
                {
                    ParameterQueueClientCount++;

                    if (context.Parameter.Member.Name == "QueueToQueueAsync")
                    {
                        // demonstrates how context can be used to create a custom client
                        // for a particular method or parameter binding
                    }
                }

                return base.CreateCloudQueueClient(context);
            }

            public override CloudTableClient CreateCloudTableClient(StorageClientFactoryContext context)
            {
                TotalTableClientCount++;

                if (context.Parameter != null)
                {
                    ParameterTableClientCount++;
                }

                return base.CreateCloudTableClient(context);
            }
        }

        public class TestFixture : IDisposable
        {
            public TestFixture()
            {
                JobHostConfiguration config = new JobHostConfiguration();
                StorageAccount = CloudStorageAccount.Parse(config.StorageConnectionString);
            }

            public CloudStorageAccount StorageAccount
            {
                get;
                private set;
            }

            public void Dispose()
            {
                CloudBlobClient blobClient = StorageAccount.CreateCloudBlobClient();
                foreach (var testContainer in blobClient.ListContainers(TestArtifactsPrefix))
                {
                    testContainer.Delete();
                }

                CloudQueueClient queueClient = StorageAccount.CreateCloudQueueClient();
                foreach (var testQueue in queueClient.ListQueues(TestArtifactsPrefix))
                {
                    testQueue.Delete();
                }
            }
        }

        private class TestExceptionHandler : IWebJobsExceptionHandler
        {
            public ICollection<ExceptionDispatchInfo> UnhandledExceptionInfos { get; private set; }
            public ICollection<ExceptionDispatchInfo> TimeoutExceptionInfos { get; private set; }

            public void Initialize(JobHost host)
            {
                UnhandledExceptionInfos = new List<ExceptionDispatchInfo>();
                TimeoutExceptionInfos = new List<ExceptionDispatchInfo>();
            }

            public Task OnTimeoutExceptionAsync(ExceptionDispatchInfo exceptionInfo, TimeSpan timeoutGracePeriod)
            {
                TimeoutExceptionInfos.Add(exceptionInfo);
                return Task.FromResult(0);
            }

            public Task OnUnhandledExceptionAsync(ExceptionDispatchInfo exceptionInfo)
            {
                UnhandledExceptionInfos.Add(exceptionInfo);
                return Task.FromResult(0);
            }
        }
    }
}
