// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.ApiHub;
using Microsoft.Azure.ApiHub.Table.Internal;
using Microsoft.Azure.WebJobs.Extensions.ApiHub.Common;
using Microsoft.Azure.WebJobs.Script.Config;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public static class ApiHubTestHelper
    {
        public const int EntityId1 = 1;
        public const int EntityId2 = 2;
        public const int EntityId3 = 3;
        public const int EntityId4 = 4;
        public const int EntityId5 = 5;
        public const string TextArg = "text";

        private const string Key = "AzureWebJobsSql";
        private const string DataSetName = "default";
        private const string TableName = "SampleTable";
        private const string PrimaryKeyColumn = "Id";

        private static readonly ScriptSettingsManager SettingsManager = ScriptSettingsManager.Instance;

        public static void SetDefaultConnectionFactory()
        {
            // Setup the default ApiHub connection factory to use an actual SqlAzure connector
            // if the AzureWebJobsSql environment variable specifies the connection string,
            // otherwise use a fake tabular connector.
            var connectionString = SettingsManager.GetSetting(Key);
            if (string.IsNullOrEmpty(connectionString))
            {
                var tableAdapter = new FakeTabularConnectorAdapter();
                tableAdapter.AddDataSet(DataSetName);
                tableAdapter.AddTable(DataSetName, TableName, primaryKey: PrimaryKeyColumn);
                ConnectionFactory.Default = new FakeConnectionFactory(tableAdapter);

                // The value doesn't really matter here - this is just so we
                // pass the Connection value validation that ScriptHost performs
                // on startup
                SettingsManager.SetSetting(Key, "TestMockSqlConnection");
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
