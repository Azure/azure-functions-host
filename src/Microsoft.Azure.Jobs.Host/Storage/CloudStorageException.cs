using System;

namespace Microsoft.Azure.Jobs.Host.Storage
{
    /// <remarks>
    /// This class exists solely to provide compatibility with the current SDK's exception structure.
    /// Once we use the latest SDK, we can remove this class entirely.
    /// </remarks>
    internal class CloudStorageException : Exception
    {
        private readonly CloudRequestResult _requestInformation;

        public CloudStorageException(CloudRequestResult requestInformation)
        {
            _requestInformation = requestInformation;
        }

        public CloudRequestResult RequestInformation
        {
            get
            {
                return _requestInformation;
            }
        }
    }
}
