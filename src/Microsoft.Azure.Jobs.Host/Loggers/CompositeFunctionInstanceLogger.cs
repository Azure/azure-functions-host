// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.Protocols;
using System;
using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    internal class CompositeFunctionInstanceLogger : IFunctionInstanceLogger
    {
        private readonly IFunctionInstanceLogger[] _loggers;

        public CompositeFunctionInstanceLogger(params IFunctionInstanceLogger[] loggers)
        {
            _loggers = loggers;
        }

        public string LogFunctionStarted(FunctionStartedMessage message)
        {
            string startedMessageId = null;

            foreach (IFunctionInstanceLogger logger in _loggers)
            {
                var messageId = logger.LogFunctionStarted(message);
                if (!String.IsNullOrEmpty(messageId))
                {
                    if (String.IsNullOrEmpty(startedMessageId))
                    {
                        startedMessageId = messageId;
                    }
                    else if (startedMessageId != messageId)
                    {
                        throw new NotSupportedException();
                    }
                }
            }

            return startedMessageId;
        }

        public void LogFunctionCompleted(FunctionCompletedMessage message)
        {
            foreach (IFunctionInstanceLogger logger in _loggers)
            {
                logger.LogFunctionCompleted(message);
            }
        }

        public void DeleteLogFunctionStarted(string startedMessageId)
        {
            foreach (IFunctionInstanceLogger logger in _loggers)
            {
                logger.DeleteLogFunctionStarted(startedMessageId);
            }
        }
    }
}
