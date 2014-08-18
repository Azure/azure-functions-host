// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host.Bindings;

namespace Microsoft.Azure.WebJobs.Host.Blobs
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
