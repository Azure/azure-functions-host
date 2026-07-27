// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Channels;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    /// <summary>
    /// A process-wide buffer for deferred log entries. A single instance lives for the lifetime of the
    /// WebHost so that buffered logs survive ScriptHost restarts and specialization. Loggers write to it
    /// and the active ScriptHost's <see cref="DeferredLogForwardingService"/> reads from it.
    /// </summary>
    internal sealed class DeferredLogSource
    {
        // A single bounded buffer shared by all loggers (writers) and the active ScriptHost's forwarding
        // service (reader). Oldest entries are dropped when full. SingleReader is false because readers can
        // briefly overlap while one ScriptHost is being orphaned and the next one is starting up.
        private readonly Channel<DeferredLogEntry> _channel = Channel.CreateBounded<DeferredLogEntry>(new BoundedChannelOptions(150)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        });

        private volatile bool _isEnabled = true;

        public ChannelReader<DeferredLogEntry> Reader => _channel.Reader;

        public bool IsEnabled => _isEnabled;

        public void Write(DeferredLogEntry entry) => _channel.Writer.TryWrite(entry);

        public void Disable()
        {
            _isEnabled = false;
            _channel.Writer.TryComplete();
        }
    }
}
