// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal interface ITableArgumentBinding : IArgumentBinding<IStorageTable>
    {
        FileAccess Access { get; }
    }
}
