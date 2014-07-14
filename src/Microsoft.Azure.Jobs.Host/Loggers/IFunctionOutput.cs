using System;
using System.IO;
using Microsoft.Azure.Jobs.Host.Timers;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    internal interface IFunctionOutput : IDisposable
    {
        ICanFailCommand UpdateCommand { get; }

        TextWriter Output { get; }

        void SaveAndClose();
    }
}
