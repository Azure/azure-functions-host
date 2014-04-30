using Microsoft.Azure.Jobs;

namespace Dashboard.Protocols
{
    internal interface IRunningHostTableReader
    {
        RunningHost[] ReadAll();
    }
}
