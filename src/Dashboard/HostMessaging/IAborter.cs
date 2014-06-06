using System;

namespace Dashboard.HostMessaging
{
    public interface IAborter
    {
        void RequestHostInstanceAbort(string queueName);

        bool HasRequestedHostInstanceAbort(string queueName);
    }
}
