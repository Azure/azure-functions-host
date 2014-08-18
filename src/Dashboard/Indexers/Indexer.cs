// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using Microsoft.Azure.WebJobs.Protocols;
using Dashboard.Data.Logs;

namespace Dashboard.Indexers
{
    internal class Indexer : IIndexer
    {
        private readonly IPersistentQueueReader<PersistentQueueMessage> _queueReader;
        private readonly IHostIndexer _hostIndexer;
        private readonly IFunctionIndexer _functionIndexer;
        private readonly IIndexerLogWriter _logWriter;

        public Indexer(IPersistentQueueReader<PersistentQueueMessage> queueReader,
            IHostIndexer hostIndexer,
            IFunctionIndexer functionIndexer,
            IIndexerLogWriter logWriter)
        {
            _queueReader = queueReader;
            _hostIndexer = hostIndexer;
            _functionIndexer = functionIndexer;
            _logWriter = logWriter;
        }

        public void Update()
        {
            const int ExceptionTitleMaxLength = 64;

            try
            {
                UpdateCore();
            }
            catch (Exception ex)
            {
                IndexerLogEntry logEntry = new IndexerLogEntry()
                {
                    Title = ex.Message,
                    Date = DateTime.UtcNow,
                    ExceptionDetails = ex.ToString()
                };

                if (logEntry.Title.Length > ExceptionTitleMaxLength)
                {
                    logEntry.Title = logEntry.Title.Substring(0, ExceptionTitleMaxLength);
                }

                // Remove any new lines from the metadata, otherwise you get a 403
                // back from the Azure SDK
                logEntry.Title = logEntry.Title.Replace("\r", "");
                logEntry.Title = logEntry.Title.Replace("\n", "");

                _logWriter.Write(logEntry);
            }
        }

        private void UpdateCore()
        {
            PersistentQueueMessage message = _queueReader.Dequeue();

            while (message != null)
            {
                Process(message);
                _queueReader.Delete(message);

                message = _queueReader.Dequeue();
            }
        }

        private void Process(PersistentQueueMessage message)
        {
            HostStartedMessage hostStartedMessage = message as HostStartedMessage;

            if (hostStartedMessage != null)
            {
                _hostIndexer.ProcessHostStarted(hostStartedMessage);
                return;
            }

            FunctionCompletedMessage functionCompletedMessage = message as FunctionCompletedMessage;

            if (functionCompletedMessage != null)
            {
                _functionIndexer.ProcessFunctionCompleted(functionCompletedMessage);
                return;
            }

            FunctionStartedMessage functionStartedMessage = message as FunctionStartedMessage;

            if (functionStartedMessage != null)
            {
                _functionIndexer.ProcessFunctionStarted(functionStartedMessage);
                return;
            }

            string errorMessage =
                String.Format(CultureInfo.InvariantCulture, "Unknown message type '{0}'.", message.Type);
            throw new InvalidOperationException(errorMessage);
        }
    }
}
