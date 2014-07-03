using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Indexers
{
    public interface IHostIndexer
    {
        void ProcessHostStarted(HostStartedMessage message);
    }
}
