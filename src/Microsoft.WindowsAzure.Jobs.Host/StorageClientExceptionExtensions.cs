using System;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    internal static class StorageClientExceptionExtensions
    {
        public static bool IsServerSideError(this StorageClientException exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException("exception");
            }

            // See http://msdn.microsoft.com/en-us/library/microsoft.windowsazure.storageclient.storageerrorcode.aspx.

            switch (exception.ErrorCode)
            {
                case StorageErrorCode.ServiceBadResponse:
                case StorageErrorCode.ServiceIntegrityCheckFailed:
                case StorageErrorCode.ServiceInternalError:
                case StorageErrorCode.ServiceTimeout:
                case StorageErrorCode.TransportError:
                    return true;
                default:
                    return false;
            }
        }
    }
}
