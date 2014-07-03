namespace Dashboard.Data
{
    internal interface IHostIndexManager
    {
        bool UpdateOrCreateIfLatest(string id, HostSnapshot snapshot);
    }
}
