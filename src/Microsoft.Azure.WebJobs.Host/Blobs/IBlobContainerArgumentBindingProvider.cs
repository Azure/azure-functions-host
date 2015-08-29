// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal interface IBlobContainerArgumentBindingProvider
    {
        IArgumentBinding<IStorageBlobContainer> TryCreate(ParameterInfo parameter);
    }
}
