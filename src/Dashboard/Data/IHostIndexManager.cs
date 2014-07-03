namespace Dashboard.Data
{
    internal interface IHostIndexManager
    {
        void UpdateOrCreateIfLatest(string id, HostSnapshot snapshot);
    }
}
