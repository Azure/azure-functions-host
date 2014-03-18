using Microsoft.WindowsAzure.Jobs;

namespace Dashboard.Protocols
{
    internal interface IRunningHostTableReader
    {
        RunningHost[] ReadAll();
    }
}
