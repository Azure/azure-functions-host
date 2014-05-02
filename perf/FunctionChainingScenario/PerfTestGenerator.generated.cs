namespace Microsoft.Azure.Jobs.Host.FunctionChainingScenario
{
	public static partial class PerfTest
	{
		public const int NumberOfGeneratedMethods = 20;

		public static void QueuePerfJob1([QueueInput(PerfQueuePrefix + "1")] string input, [QueueOutput(PerfQueuePrefix + "2")] out string output)
		{
			output = input;
		}

		public static void QueuePerfJob2([QueueInput(PerfQueuePrefix + "2")] string input, [QueueOutput(PerfQueuePrefix + "3")] out string output)
		{
			output = input;
		}

		public static void QueuePerfJob3([QueueInput(PerfQueuePrefix + "3")] string input, [QueueOutput(PerfQueuePrefix + "4")] out string output)
		{
			output = input;
		}

		public static void QueuePerfJob4([QueueInput(PerfQueuePrefix + "4")] string input, [QueueOutput(PerfQueuePrefix + "5")] out string output)
		{
			output = input;
		}

		public static void QueuePerfJob5([QueueInput(PerfQueuePrefix + "5")] string input, [QueueOutput(PerfQueuePrefix + "6")] out string output)
		{
			output = input;
		}

		public static void QueuePerfJob6([QueueInput(PerfQueuePrefix + "6")] string input, [QueueOutput(PerfQueuePrefix + "7")] out string output)
		{
			output = input;
		}

		public static void QueuePerfJob7([QueueInput(PerfQueuePrefix + "7")] string input, [QueueOutput(PerfQueuePrefix + "8")] out string output)
		{
			output = input;
		}

		public static void QueuePerfJob8([QueueInput(PerfQueuePrefix + "8")] string input, [QueueOutput(PerfQueuePrefix + "9")] out string output)
		{
			output = input;
		}

		public static void QueuePerfJob9([QueueInput(PerfQueuePrefix + "9")] string input, [QueueOutput(PerfQueuePrefix + "10")] out string output)
		{
			output = input;
		}

		public static void QueuePerfJob10([QueueInput(PerfQueuePrefix + "10")] string input, [QueueOutput(PerfQueuePrefix + "11")] out string output)
		{
			output = input;
		}

		public static void QueuePerfJob11([QueueInput(PerfQueuePrefix + "11")] string input, [QueueOutput(PerfQueuePrefix + "12")] out string output)
		{
			output = input;
		}

		public static void QueuePerfJob12([QueueInput(PerfQueuePrefix + "12")] string input, [QueueOutput(PerfQueuePrefix + "13")] out string output)
		{
			output = input;
		}

		public static void QueuePerfJob13([QueueInput(PerfQueuePrefix + "13")] string input, [QueueOutput(PerfQueuePrefix + "14")] out string output)
		{
			output = input;
		}

		public static void QueuePerfJob14([QueueInput(PerfQueuePrefix + "14")] string input, [QueueOutput(PerfQueuePrefix + "15")] out string output)
		{
			output = input;
		}

		public static void QueuePerfJob15([QueueInput(PerfQueuePrefix + "15")] string input, [QueueOutput(PerfQueuePrefix + "16")] out string output)
		{
			output = input;
		}

		public static void QueuePerfJob16([QueueInput(PerfQueuePrefix + "16")] string input, [QueueOutput(PerfQueuePrefix + "17")] out string output)
		{
			output = input;
		}

		public static void QueuePerfJob17([QueueInput(PerfQueuePrefix + "17")] string input, [QueueOutput(PerfQueuePrefix + "18")] out string output)
		{
			output = input;
		}

		public static void QueuePerfJob18([QueueInput(PerfQueuePrefix + "18")] string input, [QueueOutput(PerfQueuePrefix + "19")] out string output)
		{
			output = input;
		}

		public static void QueuePerfJob19([QueueInput(PerfQueuePrefix + "19")] string input, [QueueOutput(PerfQueuePrefix + "20")] out string output)
		{
			output = input;
		}

		public static void QueuePerfJob20([QueueInput(PerfQueuePrefix + "20")] string input, [QueueOutput(LastQueueName)] out string output)
		{
			output = input;
		}
	}
}