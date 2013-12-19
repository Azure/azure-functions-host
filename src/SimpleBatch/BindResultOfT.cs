using System;

namespace Microsoft.WindowsAzure.Jobs
{
    // Strongly typed wrapper around a result.
    // This is useful in model binders.
    // ### Call cleanup function? And how does that interfere with out parameter?
    internal class BindResult<T> : BindResult
    {
        private readonly BindResult[] _inners;
        public Action<T> Cleanup;
        public ISelfWatch _watcher;

        // this OnPostAction() will chain to inners
        public BindResult(T result, params BindResult[] inners)
        {
            _inners = inners;
            this.Result = result;
        }

        public BindResult(T result)
        {
            this.Result = result;
        }

        public override ISelfWatch Watcher
        {
            get
            {
                return base.Watcher ?? _inners[0].Watcher;
            }
        }

        public new T Result
        {
            get
            {
                BindResult x = this;
                return (T)x.Result;
            }
            set
            {
                BindResult x = this;
                x.Result = value;
            }
        }

        public override void OnPostAction()
        {
            if (Cleanup != null)
            {
                Cleanup(this.Result);
            }

            if (_inners != null)
            {
                foreach (var inner in _inners)
                {
                    inner.OnPostAction();
                }
            }
        }
    }
}
