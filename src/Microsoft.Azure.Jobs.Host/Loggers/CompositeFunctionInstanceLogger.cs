// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    internal class CompositeFunctionInstanceLogger : IFunctionInstanceLogger
    {
        private readonly IFunctionInstanceLogger[] _loggers;

        public CompositeFunctionInstanceLogger(params IFunctionInstanceLogger[] loggers)
        {
            _loggers = loggers;
        }

        public void LogFunctionStarted(FunctionStartedMessage message)
        {
            foreach (IFunctionInstanceLogger logger in _loggers)
            {
                logger.LogFunctionStarted(message);
            }
        }

        public void LogFunctionCompleted(FunctionCompletedMessage message)
        {
            foreach (IFunctionInstanceLogger logger in _loggers)
            {
                logger.LogFunctionCompleted(message);
            }
        }
    }
}
