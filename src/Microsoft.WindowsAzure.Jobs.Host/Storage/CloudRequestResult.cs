namespace Microsoft.WindowsAzure.Jobs.Host.Storage
{
    /// <remarks>
    /// This class exists solely to provide compatibility with the current SDK's exception structure.
    /// Once we use the latest SDK, we can remove this class entirely.
    /// </remarks>
    internal class CloudRequestResult
    {
        private readonly int _httpStatusCode;

        public CloudRequestResult(int httpStatusCode)
        {
            _httpStatusCode = httpStatusCode;
        }

        public int HttpStatusCode
        {
            get
            {
                return _httpStatusCode;
            }
        }
    }
}
