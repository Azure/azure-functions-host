namespace Microsoft.Azure.Jobs
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
            // $$$ Validate the names upfront where it's easy to diagnose. This can avoid cryptor 400 errors from Azure later. 
            // Rules are here: http://msdn.microsoft.com/en-us/library/windowsazure/dd135715.aspx
            return containerName + "/" + blobName;
        }
    }
}
