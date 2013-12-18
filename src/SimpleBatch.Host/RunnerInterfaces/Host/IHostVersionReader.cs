namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IHostVersionReader
    {
        HostVersion[] ReadAll();
    }
}
