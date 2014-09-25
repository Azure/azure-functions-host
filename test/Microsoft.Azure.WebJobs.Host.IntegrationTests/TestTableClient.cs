// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.IntegrationTests
{
    public class TestTableClient
    {
        public static CloudTable GetTableReference(string tableName)
        {
            var account = TestStorage.GetAccount();
            CloudTableClient client = account.CreateCloudTableClient();
            CloudTable table = client.GetTableReference(tableName);
            return table;
        }

        [DebuggerNonUserCode]
        public static void DeleteTable(string tableName)
        {
            var account = TestStorage.GetAccount();
            var client = account.CreateCloudTableClient();
            var table = client.GetTableReference(tableName);

            DeleteTable(table);
        }

        [DebuggerNonUserCode]
        public static void DeleteTable(CloudTable table)
        {
            try
            {
                table.Delete();
            }
            catch (StorageException)
            {
            }
        }
    }
}
