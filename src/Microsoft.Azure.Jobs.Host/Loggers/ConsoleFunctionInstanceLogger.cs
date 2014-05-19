using System;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    internal class ConsoleFunctionInstanceLogger : IFunctionInstanceLogger
    {
        public void LogFunctionStarted(FunctionStartedSnapshot snapshot)
        {
            Console.WriteLine("Executing: '{0}' because {1}", snapshot.FunctionShortName, snapshot.Reason);
        }

        public void LogFunctionCompleted(FunctionCompletedSnapshot snapshot)
        {
            if (!snapshot.Succeeded)
            {
                var oldColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  Function had errors. See Azure Jobs dashboard for details. Instance id is {0}", snapshot.FunctionInstanceId);
                Console.ForegroundColor = oldColor;
            }
        }
    }
}
