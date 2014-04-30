namespace Microsoft.Azure.Jobs
{
    // Listening to external blobs is slow. But when we write a blob ourselves, we can hook the notifations
    // so that we detect the new blob immediately without polling.
    internal interface INotifyNewBlob
    {
        void Notify(BlobWrittenMessage msg);
    }
}
