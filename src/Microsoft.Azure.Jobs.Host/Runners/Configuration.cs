using System;
using System.Collections.Generic;

namespace Microsoft.Azure.Jobs
{
    internal class Configuration : IConfiguration
    {
        private IList<Type> _cloudBlobStreamBinderTypes = new List<Type>();

        private IList<ICloudBlobBinderProvider> _blobBinders = new List<ICloudBlobBinderProvider>();

        private IList<ICloudTableBinderProvider> _tableBinders = new List<ICloudTableBinderProvider>();

        public IList<Type> CloudBlobStreamBinderTypes
        {
            get { return _cloudBlobStreamBinderTypes; }
        }

        public IList<ICloudBlobBinderProvider> BlobBinders
        {
            get { return _blobBinders; }
        }

        public IList<ICloudTableBinderProvider> TableBinders
        {
            get { return _tableBinders; }
        }

        public INameResolver NameResolver { get; set; }
    }
}
