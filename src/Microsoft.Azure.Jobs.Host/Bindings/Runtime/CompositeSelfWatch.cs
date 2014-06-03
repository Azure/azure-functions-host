using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Azure.Jobs.Host.Bindings.Runtime
{
    internal class CompositeSelfWatch : ISelfWatch
    {
        private ConcurrentDictionary<string, IWatchable> _watchables =
            new ConcurrentDictionary<string, IWatchable>();

        public void Add(string name, IWatchable watchable)
        {
            _watchables.AddOrUpdate(name, watchable, (ignore1, ignore2) => watchable);
        }

        public string GetStatus()
        {
            if (_watchables.Count == 0)
            {
                return null;
            }

            // Show selfwatch from objects we've handed out. 
            StringBuilder builder = new StringBuilder();
            builder.AppendFormat("Created {0} object(s):", _watchables.Count);
            builder.AppendLine();

            foreach (KeyValuePair<string, IWatchable> watchable in _watchables)
            {
                builder.Append(watchable.Key);

                if (watchable.Value != null && watchable.Value.Watcher != null)
                {
                    builder.Append(" ");
                    builder.Append(watchable.Value.Watcher.GetStatus());
                }

                builder.AppendLine();
            }

            return SelfWatch.EncodeSelfWatchStatus(builder.ToString());
        }
    }
}
