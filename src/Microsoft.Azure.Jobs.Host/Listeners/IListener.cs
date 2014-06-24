using System;

namespace Microsoft.Azure.Jobs.Host.Listeners
{
    internal interface IListener : IDisposable
    {
        void Start();

        void Stop();
    }
}
