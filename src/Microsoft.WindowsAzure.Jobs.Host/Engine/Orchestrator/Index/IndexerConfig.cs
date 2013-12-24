using System.Collections.Generic;

namespace Microsoft.WindowsAzure.Jobs
{
    // IConfiguration implementation for Registering functions. 
    internal class IndexerConfig : IConfiguration
    {
        private List<ICloudBlobBinderProvider> _blobBinders = new List<ICloudBlobBinderProvider>();
        private List<ICloudTableBinderProvider> _tableBinders = new List<ICloudTableBinderProvider>();
        private List<ICloudBinderProvider> _binders = new List<ICloudBinderProvider>();

        public IList<ICloudBlobBinderProvider> BlobBinders
        {
            get { return _blobBinders; }
        }

        public IList<ICloudTableBinderProvider> TableBinders
        {
            get { return _tableBinders; }
        }

        public IList<ICloudBinderProvider> Binders
        {
            get { return _binders; }
        }
    }
}
