// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class TableEntityContext
    {
        public CloudTable Table { get; set; }

        public string PartitionKey { get; set; }

        public string RowKey { get; set; }

        public string ToInvokeString()
        {
            return Table.Name + "/" + PartitionKey + "/" + RowKey;
        }
    }
}
