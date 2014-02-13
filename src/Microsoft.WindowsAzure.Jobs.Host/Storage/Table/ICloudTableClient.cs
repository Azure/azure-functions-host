namespace Microsoft.WindowsAzure.Jobs.Host.Storage.Table
{
    internal interface ICloudTableClient
    {
        ICloudTable GetTableReference(string tableName);
    }
}
