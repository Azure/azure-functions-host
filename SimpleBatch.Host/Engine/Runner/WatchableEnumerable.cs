using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using RunnerInterfaces;
using SimpleBatch;

namespace RunnerHost
{
    // Tracks number of times MoveNext() is called on the enumerator.
    public class WatchableEnumerable<T> : IEnumerable<T>, ISelfWatch
    {
        private IEnumerable<T> _inner;

        public WatchableEnumerable(IEnumerable<T> inner)
        {
            _inner = inner;
        }

        public IEnumerator<T> GetEnumerator()
        {
            return new WatchableEnumerator { _inner = _inner.GetEnumerator(), _parent = this };
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        class WatchableEnumerator : IEnumerator<T>
        {
            public WatchableEnumerable<T> _parent;
            public IEnumerator<T> _inner;

            public T Current
            {
                get { return _inner.Current; }
            }

            public void Dispose()
            {
                _inner.Dispose();
            }

            object System.Collections.IEnumerator.Current
            {
                get
                {
                    System.Collections.IEnumerator x = _inner;
                    return x.Current;
                }
            }

            public bool MoveNext()
            {
                // This is the heart of the watcher. 
                _parent._counter++;
                return _inner.MoveNext();
            }

            public void Reset()
            {
                _inner.Reset();
            }
        }

        private volatile int _counter;

        public string GetStatus()
        {
            // $$$ This is off by 1 since MoveNext starts before the first item.
            long x = _counter;
            return string.Format("Read {0} items", _counter);
        }
    }
}