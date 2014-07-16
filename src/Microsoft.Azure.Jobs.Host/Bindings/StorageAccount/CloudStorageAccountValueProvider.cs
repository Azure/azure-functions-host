// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Bindings.StorageAccount
{
    internal class CloudStorageAccountValueProvider : IValueProvider
    {
        private readonly CloudStorageAccount _account;

        public CloudStorageAccountValueProvider(CloudStorageAccount account)
        {
            _account = account;
        }

        public Type Type
        {
            get { return typeof(CloudStorageAccount); }
        }

        public object GetValue()
        {
            return _account;
        }

        public string ToInvokeString()
        {
            return null;
        }
    }
}
