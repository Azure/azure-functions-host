// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.ApiHub.Sdk;
using Microsoft.Azure.ApiHub.Sdk.Table;
using Microsoft.Azure.ApiHub.Sdk.Table.Internal;
using Microsoft.Azure.WebJobs.Extensions.ApiHub.Common;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.ApiHub
{
    public static class ApiHubTestHelper
    {
        public const int EntityId1 = 1;
        public const int EntityId2 = 2;
        public const int EntityId3 = 3;
        public const int EntityId4 = 4;
        public const int EntityId5 = 5;
        public const string TextArg = "text";
        public const string EntityIdArg = "entityId";

        private const string Key = "AzureWebJobsSql";
        private const string DataSetName = "default";
        private const string TableName = "SampleTable";
        private const string PrimaryKeyColumn = "Id";
        private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        private static readonly Random Random = new Random();

        public static void SetDefaultConnectionFactory()
        {
            // Setup the default ApiHub connection factory to use an actual SqlAzure connector 
            // if the AzureWebJobsSql environment variable specifies the connection string,
            // otherwise use a fake tabular connector.
            var connectionString = Environment.GetEnvironmentVariable(Key);
            if (string.IsNullOrEmpty(connectionString))
            {
                var tableAdapter = new FakeTabularConnectorAdapter();
                tableAdapter.AddDataSet(DataSetName);
                tableAdapter.AddTable(DataSetName, TableName, primaryKey: PrimaryKeyColumn);
                ConnectionFactory.Default = new FakeConnectionFactory(tableAdapter);

                // The value doesn't really matter here - this is just so we
                // pass the Connection value validation that ScriptHost performs
                // on startup
                Environment.SetEnvironmentVariable(Key, "TestMockSqlConnection");
            }
            else
            {
                // 'SampleTable' must be created prior to running the tests:
                //      CREATE TABLE SampleTable
                //      (
                //          Id int NOT NULL,
                //          Text nvarchar(10) NULL
                //          CONSTRAINT PK_Id PRIMARY KEY(Id)
                //      )
            }
        }

        public static string NewRandomString()
        {
            return new string(
                Enumerable.Repeat('x', 10) // nvarchar(10)
                    .Select(c => Chars[Random.Next(Chars.Length)])
                    .ToArray());
        }

        public static async Task EnsureEntityAsync(int entityId, string text = null)
        {
            var connection = ConnectionFactory.Default.CreateConnection(Key);
            var tableClient = connection.CreateTableClient();
            var dataSet = tableClient.GetDataSetReference();
            var table = dataSet.GetTableReference<SampleEntity>(TableName);

            await table.DeleteEntityAsync(entityId.ToString());

            await table.CreateEntityAsync(
                new SampleEntity
                {
                    Id = entityId,
                    Text = text ?? "x"
                });
        }

        public static async Task DeleteEntityAsync(int entityId)
        {
            var connection = ConnectionFactory.Default.CreateConnection(Key);
            var tableClient = connection.CreateTableClient();
            var dataSet = tableClient.GetDataSetReference();
            var table = dataSet.GetTableReference<SampleEntity>(TableName);

            await table.DeleteEntityAsync(entityId.ToString());
        }

        public static async Task AssertTextUpdatedAsync(string expectedText, int entityId)
        {
            var connection = ConnectionFactory.Default.CreateConnection(Key);
            var tableClient = connection.CreateTableClient();
            var dataSet = tableClient.GetDataSetReference();
            var table = dataSet.GetTableReference<SampleEntity>(TableName);
            var entity = await table.GetEntityAsync(entityId.ToString());

            Assert.NotNull(entity);
            Assert.Equal(expectedText, entity.Text);
        }

        private class SampleEntity
        {
            public int Id { get; set; }
            public string Text { get; set; }
        }

        private class FakeConnectionFactory : ConnectionFactory
        {
            public FakeConnectionFactory(FakeTabularConnectorAdapter tableAdapter)
            {
                TableAdapter = tableAdapter;
            }

            private FakeTabularConnectorAdapter TableAdapter { get; }

            public override Connection CreateConnection(string key)
            {
                return new FakeConnection(TableAdapter);
            }

            private class FakeConnection : Connection
            {
                public FakeConnection(FakeTabularConnectorAdapter tableAdapter)
                {
                    TableAdapter = tableAdapter;
                }

                private FakeTabularConnectorAdapter TableAdapter { get; }

                public override ITableClient CreateTableClient()
                {
                    return new TableClient(TableAdapter);
                }
            }
        }
    }
}
