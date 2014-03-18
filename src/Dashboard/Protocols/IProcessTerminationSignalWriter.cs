using System;

namespace Dashboard.Protocols
{
    internal interface IProcessTerminationSignalWriter
    {
        void RequestTermination(Guid hostInstanceId);
    }
}
