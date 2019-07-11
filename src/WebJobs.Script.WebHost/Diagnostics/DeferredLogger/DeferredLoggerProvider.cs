// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class DeferredLoggerProvider : ILoggerProvider, IDeferredLogSource, ISupportExternalScope
    {
        private readonly Channel<DeferredLogMessage> _loggerChannel;

        private IExternalScopeProvider _scopeProvider;
        private bool _disposed;

        public DeferredLoggerProvider()
            : this(1000)
        {
        }

        internal DeferredLoggerProvider(int bufferSize)
        {
            _loggerChannel = Channel.CreateBounded<DeferredLogMessage>(new BoundedChannelOptions(bufferSize)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                AllowSynchronousContinuations = true,
                SingleWriter = true,
                SingleReader = false
            });
        }

        public ChannelReader<DeferredLogMessage> LogChannel => _loggerChannel.Reader;

        public ILogger CreateLogger(string categoryName) => new DeferredLogger(categoryName, _loggerChannel.Writer, _scopeProvider);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _loggerChannel.Writer.Complete();
                _disposed = true;
            }
        }
    }
}