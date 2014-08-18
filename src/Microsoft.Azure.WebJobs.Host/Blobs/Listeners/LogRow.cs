// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    // Describes an entry in the storage log
    internal class LogRow
    {
        public static LogRow Parse(string value)
        {
            string[] parts = value.Split(';');

            var x = new LogRow();
            x.RequestStartTime = DateTime.Parse(parts[(int)LogColumnId.RequestStartTime], CultureInfo.InvariantCulture);

            ServiceType serviceType;
            Enum.TryParse<ServiceType>(parts[(int)LogColumnId.ServiceType], out serviceType);
            x.ServiceType = serviceType;

            OperationType operationType;
            Enum.TryParse<OperationType>(parts[(int)LogColumnId.OperationType], out operationType);
            x.OperationType = operationType;

            x.RequestedObjectKey = parts[(int)LogColumnId.RequestedObjectKey];

            return x;
        }

        public DateTime RequestStartTime { get; set; }
        public OperationType OperationType { get; set; }
        public ServiceType ServiceType { get; set; }
        public string RequestedObjectKey { get; set; }

        // Null if not a blob. 
        public BlobPath ToPath()
        {
            if (ServiceType != ServiceType.Blob)
            {
                return null;
            }

            // key is "/account/container/blob"
            // - it's enclosed in quotes 
            // - first token is the account name
            string key = this.RequestedObjectKey;

            int x = key.IndexOf('/', 2); // skip past opening quote (+1) and opening / (+1)
            if (x > 0)
            {
                int start = x + 1;
                string path = key.Substring(start, key.Length - start - 1); // -1 for closing quote
                return BlobPath.Parse(path);
            }
            return null;
        }
    }
}
