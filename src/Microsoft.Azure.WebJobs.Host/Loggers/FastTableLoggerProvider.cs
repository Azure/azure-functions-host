// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    // Disable most SDK logging. Useful when we don't have a storage account and Fast-logging is enabled. 
    // This wires in an output logger to aide in capturing the TextWriter log output in memory and saving to the fast tables. 
    internal class FastTableLoggerProvider : 
        IHostInstanceLoggerProvider, 
        IFunctionInstanceLoggerProvider, 
        IFunctionOutputLoggerProvider,
        IFunctionOutputLogger
    {
        private IHostInstanceLogger _hostInstanceLogger;
        private IFunctionInstanceLogger _nullInstanceLogger = new NullInstanceLogger();

        public FastTableLoggerProvider()
        {
            _hostInstanceLogger = new NullHostInstanceLogger();
        }

        Task<IFunctionOutputLogger> IFunctionOutputLoggerProvider.GetAsync(CancellationToken cancellationToken)
        {
            IFunctionOutputLogger logger = this;
            return Task.FromResult(logger);
        }

        Task<IHostInstanceLogger> IHostInstanceLoggerProvider.GetAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_hostInstanceLogger);
        }

        Task<IFunctionInstanceLogger> IFunctionInstanceLoggerProvider.GetAsync(CancellationToken cancellationToken)
        {
            // No instance loggin
            return Task.FromResult<IFunctionInstanceLogger>(_nullInstanceLogger);
        }

        Task<IFunctionOutputDefinition> IFunctionOutputLogger.CreateAsync(IFunctionInstance instance, CancellationToken cancellationToken)
        {
            IFunctionOutputDefinition x = new PerFunc();
            return Task.FromResult(x);
        }
        
        private class NullInstanceLogger : IFunctionInstanceLogger
        {
            public Task DeleteLogFunctionStartedAsync(string startedMessageId, CancellationToken cancellationToken)
            {
                return Task.FromResult(0);
            }

            public Task LogFunctionCompletedAsync(FunctionCompletedMessage message, CancellationToken cancellationToken)
            {
                return Task.FromResult(0);
            }

            public Task<string> LogFunctionStartedAsync(FunctionStartedMessage message, CancellationToken cancellationToken)
            {
                return Task.FromResult(string.Empty);
            }
        }

        private class PerFunc : IFunctionOutputDefinition, IFunctionOutput
        {
            private readonly MemoryStream _ms = new MemoryStream();
            private readonly TextWriter _writer;

            public PerFunc()
            {
                _writer = new StreamWriter(_ms);
            }

            public TextWriter Output
            {
                get
                {
                    return _writer;
                }
            }

            // These can be null. They're copied to the old log messages.
            // They're magically in sync with where the fuctions write. 

            public LocalBlobDescriptor OutputBlob
            {
                get
                {
                    return null;
                }
            }

            public LocalBlobDescriptor ParameterLogBlob
            {
                get
                {
                    return null;
                }
            }

            public IRecurrentCommand UpdateCommand
            {
                get
                {
                    return null;
                }
            }

            public IFunctionOutput CreateOutput()
            {
                return this;
            }

            public IRecurrentCommand CreateParameterLogUpdateCommand(IReadOnlyDictionary<string, IWatcher> watches, TraceWriter trace)
            {
                return null;
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "_writer")]
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "_ms")]
            public void Dispose()
            {                
            }

            public Task SaveAndCloseAsync(FunctionInstanceLogEntry item, CancellationToken cancellationToken)
            {
                if (item != null)
                {
                    var str = Encoding.UTF8.GetString(_ms.ToArray());

                    // Truncate. 
                    if (str.Length > FunctionInstanceLogEntry.MaxLogOutputLength)
                    {                        
                        // 0123456789
                        // abcdefghij
                        str = "..." + str.Substring(str.Length - (FunctionInstanceLogEntry.MaxLogOutputLength - 3));
                    }

                    item.LogOutput = str;
                }

                return Task.FromResult(0);
            }
        }
    }
}