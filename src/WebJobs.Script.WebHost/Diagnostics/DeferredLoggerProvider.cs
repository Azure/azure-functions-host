// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class DeferredLoggerProvider : ILoggerProvider, IDeferredLogSource
    {
        private readonly IScriptWebHostEnvironment _scriptEnvironment;
        private readonly BufferBlock<DeferredLogMessage> _buffer;
        private bool _disposed;

        public DeferredLoggerProvider(IScriptWebHostEnvironment scriptEnvironment)
            : this(scriptEnvironment, 1000)
        {
        }

        internal DeferredLoggerProvider(IScriptWebHostEnvironment scriptEnvironment, int bufferSize)
        {
            _scriptEnvironment = scriptEnvironment;

            _buffer = new BufferBlock<DeferredLogMessage>(new DataflowBlockOptions
            {
                BoundedCapacity = bufferSize,
                EnsureOrdered = true
            });
        }

        public ISourceBlock<DeferredLogMessage> LogBuffer => _buffer;

        public ILogger CreateLogger(string categoryName) => new DeferredLogger(categoryName, _buffer, _scriptEnvironment);

        public void Dispose()
        {
            if (!_disposed)
            {
                LogBuffer.Complete();
                _disposed = true;
            }
        }
    }
}
