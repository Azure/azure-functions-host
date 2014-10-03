// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
