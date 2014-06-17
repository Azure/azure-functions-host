using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Azure.Jobs.Host.Blobs.Bindings
{
    internal class BoundBlobPath : IBindableBlobPath
    {
        private readonly BlobPath _innerPath;

        public BoundBlobPath(BlobPath innerPath)
        {
            _innerPath = innerPath;
        }

        public string ContainerNamePattern
        {
            get { return _innerPath.ContainerName; }
        }

        public string BlobNamePattern
        {
            get { return _innerPath.BlobName; }
        }

        public bool IsBound
        {
            get { return true; }
        }

        public IEnumerable<string> ParameterNames
        {
            get { return Enumerable.Empty<string>(); }
        }

        public BlobPath Bind(IReadOnlyDictionary<string, object> bindingData)
        {
            return _innerPath;
        }

        public override string ToString()
        {
            return _innerPath.ToString();
        }
    }
}
