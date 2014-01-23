using System;
using System.Globalization;
using System.IO;

namespace Microsoft.WindowsAzure.Jobs
{
    // Function lives on some blob. 
    // Download this to convert to a LocalFunctionLocation
    internal class RemoteFunctionLocation : FileFunctionLocation
    {
        // Base class has the account connection string. 
        public CloudBlobPath DownloadSource { get; set; }

        // For convenience, return Account,Container,Blob as a single unit. 
        public CloudBlobDescriptor GetBlob()
        {
            return new CloudBlobDescriptor
            {
                AccountConnectionString = this.AccountConnectionString,
                BlobName = this.DownloadSource.BlobName,
                ContainerName = this.DownloadSource.ContainerName
            };
        }

        public override string GetId()
        {
            return String.Format(CultureInfo.InvariantCulture, @"{0}\{1}\{2}", GetBlob().GetId(), TypeName, MethodName);
        }

        // Read a file from the function's location. 
        public override string ReadFile(string filename)
        {
            var container = this.GetBlob().GetContainer();
            var blob = container.GetBlobReference(filename);
            string content = BlobClient.ReadBlob(blob);

            return content;
        }

        // Assume caller has download the remote location to localDirectoryCopy
        // The container of the remote loc should be downloaded into the same directory as localCopy
        public LocalFunctionLocation GetAsLocal(string localDirectoryCopy)
        {
            string assemblyEntryPoint = Path.Combine(localDirectoryCopy, this.DownloadSource.BlobName);

            return new LocalFunctionLocation
            {
                DownloadSource = this.DownloadSource,
                AssemblyPath = assemblyEntryPoint,
                AccountConnectionString = AccountConnectionString,
                MethodName = this.MethodName,
                TypeName = this.TypeName
            };
        }
    }
}
