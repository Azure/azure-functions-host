using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Triggers
{
    internal class ParameterizedBlobPathSource : IBlobPathSource
    {
        private readonly string _containerNamePattern;
        private readonly string _blobNamePattern;
        private readonly IReadOnlyList<string> _parameterNames;

        public ParameterizedBlobPathSource(string containerNamePattern, string blobNamePattern,
            IReadOnlyList<string> parameterNames)
        {
            Debug.Assert(parameterNames.Count > 0);

            _containerNamePattern = containerNamePattern;
            _blobNamePattern = blobNamePattern;
            _parameterNames = parameterNames;
        }

        public string ContainerNamePattern
        {
            get { return _containerNamePattern; }
        }

        public string BlobNamePattern
        {
            get { return _blobNamePattern; }
        }

        public IEnumerable<string> ParameterNames
        {
            get { return _parameterNames; }
        }

        public IReadOnlyDictionary<string, object> CreateBindingData(BlobPath actualBlobPath)
        {
            if (actualBlobPath == null)
            {
                return null;
            }

            return BindingDataPath.CreateBindingData(ToString(), actualBlobPath.ToString());
        }

        public override string ToString()
        {
            return _containerNamePattern + "/" + _blobNamePattern;
        }
    }
}
