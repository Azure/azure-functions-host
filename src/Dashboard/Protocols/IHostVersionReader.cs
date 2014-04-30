using Microsoft.Azure.Jobs.Host.Protocols;

namespace Dashboard.Protocols
{
    internal interface IHostVersionReader
    {
        HostVersion[] ReadAll();
    }
}
