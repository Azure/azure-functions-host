namespace Microsoft.WindowsAzure.Jobs
{
    internal static class BinderExExtensions
    {
        // Get a stream for the given blob. The storage account is relative to binder.AccountConnetionString,
        // and the container and blob name are specified.
        public static BindResult<T> BindReadStream<T>(this IBinderEx binder, string containerName, string blobName)
        {
            return binder.Bind<T>(new BlobInputAttribute(Combine(containerName, blobName)));
        }

        public static BindResult<T> BindWriteStream<T>(this IBinderEx binder, string containerName, string blobName)
        {
            return binder.Bind<T>(new BlobOutputAttribute(Combine(containerName, blobName)));
        }

        private static string Combine(string containerName, string blobName)
        {
            return BinderExtensions.Combine(containerName, blobName);
        }
    }
}
