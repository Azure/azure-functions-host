using System;

namespace Microsoft.Azure.Jobs.Host.Listeners
{
    internal interface ISharedListener<TFilter, TTriggerValue> : IDisposable
    {
        void Register(TFilter listenData, ITriggerExecutor<TTriggerValue> triggerExecutor);

        void EnsureAllStarted();

        void EnsureAllStopped();

        void EnsureAllDisposed();
    }
}
