namespace Microsoft.Azure.Jobs
{
    // Interface to request if you want both reading and writing. 
    // This is better than getting a separate reader and writer because this allows them to coordinate
    // on flushing writes before doing reads. Separate table objects may get out of sync.
    internal interface IAzureTable : IAzureTableReader, IAzureTableWriter
    {
    }
}
