// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal class ParameterizedBlobPath : IBindableBlobPath
    {
        private readonly BindingTemplate _containerNameTemplate;
        private readonly BindingTemplate _blobNameTemplate;

        public ParameterizedBlobPath(BindingTemplate containerNameTemplate, BindingTemplate blobNameTemplate)
        {
            Debug.Assert(containerNameTemplate != null);
            Debug.Assert(blobNameTemplate != null);

            _containerNameTemplate = containerNameTemplate;
            _blobNameTemplate = blobNameTemplate;
        }

        public string ContainerNamePattern
        {
            get { return _containerNameTemplate.Pattern; }
        }

        public string BlobNamePattern
        {
            get { return _blobNameTemplate.Pattern; }
        }

        public bool IsBound
        {
            get { return false; }
        }

        public IEnumerable<string> ParameterNames
        {
            get { return _containerNameTemplate.ParameterNames.Concat(_blobNameTemplate.ParameterNames); }
        }

        public BlobPath Bind(IReadOnlyDictionary<string, object> bindingData)
        {
            IReadOnlyDictionary<string, string> parameters = BindingDataPathHelper.ConvertParameters(bindingData);
            string containerName = _containerNameTemplate.Bind(parameters);
            string blobName = _blobNameTemplate.Bind(parameters);

            BlobClient.ValidateContainerName(containerName);
            BlobClient.ValidateBlobName(blobName);

            return new BlobPath(containerName, blobName);
        }

        public override string ToString()
        {
            return _containerNameTemplate.Pattern + "/" + _blobNameTemplate.Pattern;
        }
    }
}
