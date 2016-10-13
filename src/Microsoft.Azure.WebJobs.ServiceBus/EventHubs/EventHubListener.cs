// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    // Created from the EventHubTrigger attribute to listen on the EventHub. 
    internal sealed class EventHubListener : IListener, IEventProcessorFactory
    {
        private readonly ITriggeredFunctionExecutor _executor;
        private readonly EventProcessorHost _eventListener;
        private readonly bool _singleDispatch;
        private readonly EventProcessorOptions _options;

        public EventHubListener(ITriggeredFunctionExecutor executor, EventProcessorHost eventListener, EventProcessorOptions options, bool single)
        {
            this._executor = executor;
            this._eventListener = eventListener;
            this._singleDispatch = single;
            this._options = options;
        }

        void IListener.Cancel()
        {
            this.StopAsync(CancellationToken.None).Wait();
        }

        void IDisposable.Dispose() // via IListener
        {
            // nothing to do. 
        }

        // This will get called once when starting the JobHost. 
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return _eventListener.RegisterEventProcessorFactoryAsync(this, _options);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return _eventListener.UnregisterEventProcessorAsync();
        }
        
        // This will get called per-partition. 
        IEventProcessor IEventProcessorFactory.CreateEventProcessor(PartitionContext context)
        {
            return new Listener(this);
        }

        // We get a new instance each time Start() is called. 
        // We'll get a listener per partition - so they can potentialy run in parallel even on a single machine.
        private class Listener : IEventProcessor
        {
            private readonly EventHubListener _parent;
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();

            public Listener(EventHubListener parent)
            {
                this._parent = parent;
            }

            public async Task CloseAsync(PartitionContext context, CloseReason reason)
            {
                this._cts.Cancel(); // Signal interuption to ProcessEventsAsync()

                // Finish listener
                if (reason == CloseReason.Shutdown)
                {
                    await context.CheckpointAsync();
                }
            }

            public Task OpenAsync(PartitionContext context)
            {
                return Task.FromResult(0);
            }

            public async Task ProcessEventsAsync(PartitionContext context, IEnumerable<EventData> messages)
            {
                EventHubTriggerInput value = new EventHubTriggerInput
                {
                    Events = messages.ToArray(),
                    Context = context
                };

                // Single dispatch 
                if (_parent._singleDispatch)
                {
                    int len = value.Events.Length;

                    List<Task> dispatches = new List<Task>();
                    for (int i = 0; i < len; i++)
                    {
                        if (_cts.IsCancellationRequested)
                        {
                            // If we stopped the listener, then we may lose the lease and be unable to checkpoint. 
                            // So skip running the rest of the batch. The new listener will pick it up. 
                            continue;
                        }
                        else
                        {
                            TriggeredFunctionData input = new TriggeredFunctionData
                            {
                                ParentId = null,
                                TriggerValue = value.GetSingleEventTriggerInput(i)
                            };
                            Task task = this._parent._executor.TryExecuteAsync(input, _cts.Token);
                            dispatches.Add(task);
                        }
                    }

                    // Drain the whole batch before taking more work
                    if (dispatches.Count > 0)
                    {
                        await Task.WhenAll(dispatches);
                    }
                }
                else
                {
                    // Batch dispatch

                    TriggeredFunctionData input = new TriggeredFunctionData
                    {
                        ParentId = null,
                        TriggerValue = value
                    };

                    FunctionResult result = await this._parent._executor.TryExecuteAsync(input, CancellationToken.None);
                }

                bool hasEvents = false;
                // Dispose all messages to help with memory pressure. If this is missed, the finalizer thread will still get them. 
                foreach (var message in messages)
                {
                    hasEvents = true;
                    message.Dispose();
                }

                // Don't checkpoint if no events. This can reset the sequence counter to 0. 
                if (hasEvents)
                {
                    // There are lots of reasons this could fail. That just means that events will get double-processed, which is inevitable
                    // with event hubs anyways. 
                    // For example, it could fail if we lost the lease. That could happen if we failed to renew it due to CPU starvation or an inability 
                    // to make the outbound network calls to renew. 
                    await context.CheckpointAsync();
                }
            }
        } // end class Listener 
    }
}