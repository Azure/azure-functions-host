// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.Jobs.Host.Protocols;
using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    internal class ConsoleFunctionInstanceLogger : IFunctionInstanceLogger
    {
        public string LogFunctionStarted(FunctionStartedMessage message)
        {
            Console.WriteLine("Executing: '{0}' because {1}", message.Function.ShortName, message.FormatReason());
            return null;
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

        public void DeleteLogFunctionStarted(string startedMessageId)
        {
            // Intentionally left blank to avoid too much verbosity
        }
    }
}
