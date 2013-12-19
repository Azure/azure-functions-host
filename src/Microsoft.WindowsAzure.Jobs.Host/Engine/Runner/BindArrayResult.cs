using System;

namespace Microsoft.WindowsAzure.Jobs
{
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
