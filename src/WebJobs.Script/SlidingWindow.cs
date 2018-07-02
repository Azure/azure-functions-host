// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    public class SlidingWindow<TItem>
    {
        private readonly TimeSpan _window;
        private readonly List<Event> _events = new List<Event>();
        private readonly object _syncLock = new object();

        public SlidingWindow(TimeSpan window)
        {
            _window = window;
        }

        public IEnumerable<TItem> GetEvents()
        {
            lock (_syncLock)
            {
                RemoveExpired();

                return _events.Select(p => p.Item).ToList();
            }
        }

        public void AddEvent(TItem item)
        {
            var evt = new Event
            {
                TimeStamp = DateTime.UtcNow.Ticks,
                Item = item
            };
            lock (_syncLock)
            {
                RemoveExpired();

                _events.Add(evt);
            }
        }

        private void RemoveExpired()
        {
            _events.RemoveAll(p => IsExpired(p, _window));
        }

        internal static bool IsExpired(Event evt, TimeSpan window)
        {
            return (DateTime.UtcNow.Ticks - evt.TimeStamp) > window.Ticks;
        }

        internal class Event
        {
            public long TimeStamp { get; set; }

            public TItem Item { get; set; }
        }
    }
}