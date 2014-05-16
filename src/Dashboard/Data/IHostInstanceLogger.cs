using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Data
{
    internal interface IHostInstanceLogger
    {
        void LogHostStarted(HostStartedMessage message);
    }
}
