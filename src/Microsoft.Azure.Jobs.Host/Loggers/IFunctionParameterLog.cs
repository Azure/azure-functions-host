using System;
using Microsoft.Azure.Jobs.Host.Timers;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    internal interface IFunctionParameterLog : IDisposable
    {
        ICanFailCommand UpdateCommand { get; }

        void Close();
    }
}
