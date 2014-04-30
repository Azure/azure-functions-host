namespace Microsoft.Azure.Jobs.Host.Storage.Table
{
    internal interface ICloudTableClient
    {
        ICloudTable GetTableReference(string tableName);
    }
}
