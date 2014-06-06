using Dashboard.Data;
using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.HostMessaging
{
    public class Aborter : IAborter
    {
        private readonly IHostMessageSender _sender;
        private readonly IAbortRequestLogger _logger;

        public Aborter(IHostMessageSender sender, IAbortRequestLogger logger)
        {
            _sender = sender;
            _logger = logger;
        }

        public void RequestHostInstanceAbort(string queueName)
        {
            _sender.Enqueue(queueName, new AbortHostInstanceMessage());
            _logger.LogAbortRequest(queueName);
        }

        public bool HasRequestedHostInstanceAbort(string queueName)
        {
            return _logger.HasRequestedAbort(queueName);
        }
    }
}
