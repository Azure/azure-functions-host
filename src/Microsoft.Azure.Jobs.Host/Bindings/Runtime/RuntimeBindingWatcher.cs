using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Bindings.Runtime
{
    internal class RuntimeBindingWatcher : IWatcher
    {
        private ICollection<Tuple<ParameterDescriptor, IWatchable>> _items =
            new List<Tuple<ParameterDescriptor, IWatchable>>();
        private readonly object _itemsLock = new object();

        public void Add(ParameterDescriptor parameterDescriptor, IWatchable watchable)
        {
            lock (_itemsLock)
            {
                _items.Add(new Tuple<ParameterDescriptor, IWatchable>(parameterDescriptor, watchable));
            }
        }

        public ParameterLog GetStatus()
        {
            lock(_itemsLock)
            {
                if (_items.Count == 0)
                {
                    return null;
                }

                List<BinderParameterLogItem> logItems = new List<BinderParameterLogItem>();

                foreach (Tuple<ParameterDescriptor, IWatchable> item in _items)
                {
                    ParameterDescriptor parameterDescriptor = item.Item1;
                    IWatchable watchable = item.Item2;
                    IWatcher watcher;

                    if (watchable != null)
                    {
                        watcher = watchable.Watcher;
                    }
                    else
                    {
                        watcher = null;
                    }

                    ParameterLog itemStatus;

                    if (watcher != null)
                    {
                        itemStatus = watcher.GetStatus();
                    }
                    else
                    {
                        itemStatus = null;
                    }
                    
                    BinderParameterLogItem logItem = new BinderParameterLogItem
                    {
                        Descriptor = parameterDescriptor,
                        Log = itemStatus
                    };
                    logItems.Add(logItem);
                }

                return new BinderParameterLog { Items = logItems };
            }
        }
    }
}
