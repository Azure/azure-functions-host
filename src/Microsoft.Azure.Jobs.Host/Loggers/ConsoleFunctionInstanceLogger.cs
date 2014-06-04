using System;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    internal class ConsoleFunctionInstanceLogger : IFunctionInstanceLogger
    {
        public void LogFunctionStarted(FunctionStartedMessage message)
        {
            Console.WriteLine("Executing: '{0}' because {1}", message.FunctionShortName, message.Reason);
        }

        public void LogFunctionCompleted(FunctionCompletedMessage message)
        {
            if (!message.Succeeded)
            {
                var oldColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  Function had errors. See Azure Jobs dashboard for details. Instance id is {0}", message.FunctionInstanceId);
                Console.ForegroundColor = oldColor;
            }
        }
    }
}
