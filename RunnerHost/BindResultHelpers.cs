using System;
using RunnerInterfaces;
using SimpleBatch;

namespace RunnerHost
{
    // Helper to include a cleanup function with bind result
    class BindCleanupResult : BindResult
    {
        public Action Cleanup;
        public ISelfWatch SelfWatch;

        public override ISelfWatch Watcher
        {
            get
            {
                return this.SelfWatch ?? base.Watcher;
            }
        }

        public override void OnPostAction()
        {
            if (Cleanup != null)
            {
                Cleanup();
            }
        }
    }

    // BindResult that's an array of other results
    class BindArrayResult : BindResult
    {
        private BindResult[] _innerBinds;
        private Array _innerArray;

        public BindArrayResult(int len, Type tElement)
        {
            _innerBinds = new BindResult[len];
            _innerArray = Array.CreateInstance(tElement, len);

            this.Result = _innerArray;
        }

        public void SetBind(int idx, BindResult bind)
        {
            _innerBinds[idx] = bind;
            _innerArray.SetValue(bind.Result, idx);
        }

        public override void OnPostAction()
        {
            foreach (var bind in _innerBinds)
            {
                bind.OnPostAction();
            }
        }
    }
}