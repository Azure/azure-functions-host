// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;
using Xunit.Extensions;

namespace Microsoft.Azure.WebJobs.Host.IntegrationTests
{
    public class TableBindingTests : IDisposable
    {
        internal const string TestTableName = "TestTable";

        [Theory]
        [InlineData("FuncWithIQueryable")]
        [InlineData("FuncWithICollector")]
        [InlineData("FuncWithITableEntity")]
        [InlineData("FuncWithPocoObjectEntity")]
        [InlineData("FuncWithPocoValueEntity")]
        public void Call_WhenMissingTable_DoesntCreate(string functionName)
        {
            CloudTable table = TestTableClient.GetTableReference(TestTableName);
            var host = JobHostFactory.Create<MissingTableProgram>();
            Assert.False(table.Exists(), "table should NOT exist before the test");

            host.Call(functionName);

            Assert.False(table.Exists(), "table should NOT be created");
        }

        [Fact]
        public void Call_WhenMissingTable_Creates()
        {
            CloudTable table = TestTableClient.GetTableReference(TestTableName);
            var host = JobHostFactory.Create<MissingTableProgram>();
            Assert.False(table.Exists(), "table should NOT exist before the test");

            host.Call("FuncWithCloudTable");

            Assert.True(table.Exists(), "table must be created");
        }

        public void Dispose()
        {
            TestTableClient.DeleteTable(TestTableName);
            CloudTable table = TestTableClient.GetTableReference(TestTableName);
            Assert.False(table.Exists(), "table should be deleted after Dispose");
        }

        private class MissingTableProgram
        {
            public static void FuncWithIQueryable([Table(TestTableName)] IQueryable<CustomTableEntity> entities)
            {
                Assert.NotNull(entities);
                Assert.Empty(entities);
            }

            public static void FuncWithICollector([Table(TestTableName)] ICollector<CustomTableEntity> entities)
            {
                Assert.NotNull(entities);
            }

            public static void FuncWithITableEntity([Table(TestTableName, "PK", "RK")] CustomTableEntity entity)
            {
                Assert.Null(entity);
            }

            public static void FuncWithPocoObjectEntity([Table(TestTableName, "PK", "RK")] PocoObjectEntity entity)
            {
                Assert.Null(entity);
            }

            public static void FuncWithPocoValueEntity([Table(TestTableName, "PK", "RK")] PocoValueEntity entity)
            {
                Assert.Null(entity.Value);
            }

            public static void FuncWithCloudTable([Table(TestTableName)] CloudTable table)
            {
                Assert.NotNull(table);
            }
        }

        public class CustomTableEntity : TableEntity
        {
            public string Value { get; set; }
        }

        public class PocoObjectEntity
        {
            public string Value { get; set; }
        }

        public struct PocoValueEntity
        {
            public string Value { get; set; }
        }
    }
}
