using System;
using System.Collections.Generic;

namespace RunnerInterfaces
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

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        class WrapperEnumerator : IEnumerator<T>
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
                try
                {
                    var func = _parent.OnBefore;
                    if (func != null)
                    {
                        func();
                    }
                    return _inner.MoveNext();
                }
                finally
                {
                    var func = _parent.OnAfter;
                    if (func != null)
                    {
                        func();
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