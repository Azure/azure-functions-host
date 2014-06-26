using Microsoft.Azure.Jobs.Host.Listeners;

namespace Microsoft.Azure.Jobs.Host.Blobs.Listeners
{
    internal class SharedBlobListenerFactory : IFactory<SharedBlobListener>
    {
        private readonly ListenerFactoryContext _context;

        public SharedBlobListenerFactory(ListenerFactoryContext context)
        {
            _context = context;
        }

        public SharedBlobListener Create()
        {
            SharedBlobListener listener = new SharedBlobListener(_context.StorageAccount, _context.CancellationToken);
            _context.BlobWrittenWatcher = listener.BlobWritterWatcher;
            return listener;
        }
    }
}
