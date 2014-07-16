// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class ParameterizedBlobPath : IBindableBlobPath
    {
        private readonly string _containerNamePattern;
        private readonly string _blobNamePattern;
        private readonly IReadOnlyList<string> _parameterNames;

        public ParameterizedBlobPath(string containerNamePattern, string blobNamePattern,
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

        public bool IsBound
        {
            get { return false; }
        }

        public IEnumerable<string> ParameterNames
        {
            get { return _parameterNames; }
        }

        public BlobPath Bind(IReadOnlyDictionary<string, object> bindingData)
        {
            IReadOnlyDictionary<string, string> parameters = BindingDataPath.GetParameters(bindingData);
            string containerName = BindingDataPath.Resolve(_containerNamePattern, parameters);
            string blobName = BindingDataPath.Resolve(_blobNamePattern, parameters);

            BlobClient.ValidateContainerName(containerName);
            BlobClient.ValidateBlobName(blobName);

            return new BlobPath(containerName, blobName);
        }

        public override string ToString()
        {
            return _containerNamePattern + "/" + _blobNamePattern;
        }
    }
}
