// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host.Bindings.StorageAccount
{
    internal class StringToCloudStorageAccountConverter : IConverter<string, CloudStorageAccount>
    {
        public CloudStorageAccount Convert(string input)
        {
            return CloudStorageAccount.Parse(input);
        }
    }
}
