namespace Microsoft.WindowsAzure.Jobs
{
    public interface IHostVersionReader
    {
        HostVersion[] ReadAll();
    }
}
