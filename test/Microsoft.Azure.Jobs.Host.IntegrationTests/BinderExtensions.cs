namespace Microsoft.Azure.Jobs.Host.UnitTests
{
    internal static class BinderExtensions
    {
        public static T BindWriteStream<T>(this IBinder binder, string containerName, string blobName)
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
