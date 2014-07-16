// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class CloudBlobStreamObjectBinder<TValue> : ICloudBlobStreamObjectBinder
    {
        private readonly ICloudBlobStreamBinder<TValue> _innerBinder;

        public CloudBlobStreamObjectBinder(ICloudBlobStreamBinder<TValue> innerBinder)
        {
            _innerBinder = innerBinder;
        }

        public object ReadFromStream(Stream input)
        {
            return _innerBinder.ReadFromStream(input);
        }

        public void WriteToStream(Stream output, object value)
        {
            _innerBinder.WriteToStream((TValue)value, output);
        }
    }
}
