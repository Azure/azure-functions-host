// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text;

namespace Microsoft.Azure.WebJobs.Script
{
    public interface IProxyMetadataManager
    {
        ProxyMetadataInfo ProxyMetadata { get; }
    }
}
