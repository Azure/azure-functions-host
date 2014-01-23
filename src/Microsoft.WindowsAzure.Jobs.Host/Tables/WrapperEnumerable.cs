using System;
using System.Collections;
using System.Collections.Generic;

namespace Microsoft.WindowsAzure.Jobs
{
    // Wrap an IEnumerable<T> and invoke OnBefore/OnAfter methods around each MoveNext().
    internal class WrapperEnumerable<T> : IEnumerable<T>
    {
        private IEnumerable<T> _inner;

        public Action OnBefore { get; set; }
        public Action OnAfter { get; set; }

        public WrapperEnumerable(IEnumerable<T> inner)
        {
            _inner = inner;
        }

        public IEnumerator<T> GetEnumerator()
        {
            return new WrapperEnumerator { _inner = _inner.GetEnumerator(), _parent = this };
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        private class WrapperEnumerator : IEnumerator<T>
        {
            public WrapperEnumerable<T> _parent;
            public IEnumerator<T> _inner;

            public T Current
            {
                get { return _inner.Current; }
            }

            public void Dispose()
            {
                _inner.Dispose();
            }

            object IEnumerator.Current
            {
                get
                {
                    IEnumerator x = _inner;
                    return x.Current;
                }
            }

            public bool MoveNext()
            {
                try
                {
                    Action onBeforeAction  = _parent.OnBefore;
                    if (onBeforeAction != null)
                    {
                        onBeforeAction();
                    }
                    return _inner.MoveNext();
                }
                finally
                {
                    Action onAfterAction = _parent.OnAfter;
                    if (onAfterAction != null)
                    {
                        onAfterAction();
                    }
                }
            }

            public void Reset()
            {
                _inner.Reset();
            }
        }
    }
}
