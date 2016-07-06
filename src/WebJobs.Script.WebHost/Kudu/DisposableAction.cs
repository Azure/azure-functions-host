using System;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Kudu
{
    internal class DisposableAction : IDisposable
    {
        internal static readonly IDisposable Noop = new DisposableAction(() => { });

        private readonly Action _action;
        public DisposableAction(Action action)
        {
            _action = action;
        }

        public void Dispose()
        {
            _action();
        }
    }
}
