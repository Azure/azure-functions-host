// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    // this class handles blob pattern like {data.url}
    // which are expected to resolve to absolute blob URL
    internal class ParameterizedBlobUrl : IBindableBlobPath
    {
        private readonly BindingTemplate _urlBindingTemplate;

        public ParameterizedBlobUrl(BindingTemplate urlBindingTemplate)
        {
            Debug.Assert(urlBindingTemplate != null);

            _urlBindingTemplate = urlBindingTemplate;
        }
        // the return value here is just used for logging and does not need to be a valid binding expression
        public string ContainerNamePattern
        {
            get { return _urlBindingTemplate.Pattern + ".getContainer()"; }
        }
        // the return value here is just used for logging and does not need to be a valid binding expression
        public string BlobNamePattern
        {
            get { return _urlBindingTemplate.Pattern + ".getBlob()"; }
        }

        public bool IsBound
        {
            get { return false; }
        }

        public IEnumerable<string> ParameterNames
        {
            get { return _urlBindingTemplate.ParameterNames; }
        }

        public BlobPath Bind(IReadOnlyDictionary<string, object> bindingData)
        {
            string url = _urlBindingTemplate.Bind(bindingData);
            BlobPath pathFromUrl = BlobPath.ParseAbsUrl(url);

            // throw exceptions
            BlobClient.ValidateContainerName(pathFromUrl.ContainerName);
            BlobClient.ValidateBlobName(pathFromUrl.BlobName);

            return pathFromUrl;
        }
        public override string ToString()
        {
            return _urlBindingTemplate.Pattern;
        }
    }
}
