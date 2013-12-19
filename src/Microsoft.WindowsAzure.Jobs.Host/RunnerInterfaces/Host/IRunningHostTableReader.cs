namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IRunningHostTableReader
    {
        RunningHost[] ReadAll();
    }
}
