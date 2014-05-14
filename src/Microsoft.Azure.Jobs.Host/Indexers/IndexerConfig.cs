using System.Collections.Generic;

namespace Microsoft.Azure.Jobs
{
    // IConfiguration implementation for Registering functions. 
    internal class IndexerConfig : IConfiguration
    {
        private List<ICloudBlobBinderProvider> _blobBinders = new List<ICloudBlobBinderProvider>();
        private List<ICloudTableBinderProvider> _tableBinders = new List<ICloudTableBinderProvider>();

        public IList<ICloudBlobBinderProvider> BlobBinders
        {
            get { return _blobBinders; }
        }

        public IList<ICloudTableBinderProvider> TableBinders
        {
            get { return _tableBinders; }
        }
    }
}
