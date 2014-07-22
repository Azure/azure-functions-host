// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.Jobs.Host.Protocols;
using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    internal class PersistentQueueLogger : IHostInstanceLogger, IFunctionInstanceLogger
    {
        private readonly IPersistentQueueWriter<PersistentQueueMessage> _queueWriter;

        public PersistentQueueLogger(IPersistentQueueWriter<PersistentQueueMessage> queueWriter)
        {
            if (queueWriter == null)
            {
                throw new ArgumentNullException("queueWriter");
            }

            _queueWriter = queueWriter;
        }

        public void LogHostStarted(HostStartedMessage message)
        {
            _queueWriter.Enqueue(message);
        }

        public string LogFunctionStarted(FunctionStartedMessage message)
        {
            return _queueWriter.Enqueue(message);
        }

        public void LogFunctionCompleted(FunctionCompletedMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            _queueWriter.Enqueue(message);
        }

        public void DeleteLogFunctionStarted(string startedMessageId)
        {
            if (String.IsNullOrEmpty(startedMessageId))
            {
                throw new ArgumentNullException("startedMessageId");
            }

            _queueWriter.Delete(startedMessageId);
        }
    }
}
