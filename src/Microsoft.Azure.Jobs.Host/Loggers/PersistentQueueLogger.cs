// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Protocols;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Host.Loggers
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

        public Task LogHostStartedAsync(HostStartedMessage message, CancellationToken cancellationToken)
        {
            return _queueWriter.EnqueueAsync(message, cancellationToken);
        }

        public Task<string> LogFunctionStartedAsync(FunctionStartedMessage message, CancellationToken cancellationToken)
        {
            return _queueWriter.EnqueueAsync(message, cancellationToken);
        }

        public Task LogFunctionCompletedAsync(FunctionCompletedMessage message, CancellationToken cancellationToken)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            return _queueWriter.EnqueueAsync(message, cancellationToken);
        }

        public Task DeleteLogFunctionStartedAsync(string startedMessageId, CancellationToken cancellationToken)
        {
            if (String.IsNullOrEmpty(startedMessageId))
            {
                throw new ArgumentNullException("startedMessageId");
            }

            return _queueWriter.DeleteAsync(startedMessageId, cancellationToken);
        }
    }
}
