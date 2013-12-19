namespace Microsoft.WindowsAzure.Jobs
{
    // $$$ Not really using this. 
    // Provide basic service resolution
    internal interface IServiceContainer
    {
        T GetService<T>();
    }
}
