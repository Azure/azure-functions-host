using System.Threading;
using Microsoft.Azure.Jobs.Host.TestCommon;
using Microsoft.VisualStudio.Diagnostics.Measurement;

namespace Microsoft.Azure.Jobs.Host.FunctionChainingScenario
{
    public static partial class PerfTest
    {
        private const string HostStartMetric = "HostStart";
        private const string QueueFunctionChainMetric = "QueueChain";

        public const string PerfQueuePrefix = "%rnd%perfqueue";
        public const string FirstQueueName = PerfQueuePrefix + "start";
        public const string LastQueueName = PerfQueuePrefix + "final";

        public static CancellationTokenSource _cancelToken;

        /// <summary>
        /// Measures the time from the moment when the host is created t
        /// to the moment when the first function is invoked
        /// </summary>
        private static MeasurementBlock _startBlock;

        /// <summary>
        /// Measures the execution time of a chain of functions
        /// that pass queue messages (functions code is generated)
        /// </summary>
        private static MeasurementBlock _functionsExecutionBlock;

        private static RandomNameResolver _nameResolver = new RandomNameResolver();

        public static RandomNameResolver NameResolver
        {
            get
            {
                return _nameResolver;
            }
        }

        public static void Run(string connectionString)
        {
            _cancelToken = new CancellationTokenSource();

            _startBlock = MeasurementBlock.BeginNew(0, HostStartMetric);

            JobHostConfiguration hostConfig = new JobHostConfiguration(connectionString);
            hostConfig.NameResolver = NameResolver;
            hostConfig.TypeLocator = new SimpleTypeLocator(typeof(PerfTest));

            JobHost host = new JobHost(hostConfig);
            host.RunAndBlock(_cancelToken.Token);
        }

        public static void QueuePerfJobStart([QueueTrigger(FirstQueueName)] string input, [Queue(PerfQueuePrefix + "1")] out string output)
        {
            // When we reach here, it means that the host started and the first function is invoked
            // so we can stop the timer
            _startBlock.Dispose();

            output = input;

            _functionsExecutionBlock = MeasurementBlock.BeginNew(0, QueueFunctionChainMetric);
        }

        public static void QueuePerfJobEnd([QueueTrigger(LastQueueName)] string input)
        {
            // When we reach here, it means that all functions have completed and we can stop the timer
            _functionsExecutionBlock.Dispose();

            _cancelToken.Cancel();
        }
    }
}
