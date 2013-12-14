namespace Microsoft.WindowsAzure.Jobs
{
    public interface IRunningHostTableReader
    {
        RunningHost[] ReadAll();
    }
}
