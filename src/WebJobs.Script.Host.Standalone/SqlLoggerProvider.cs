// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Logging;

namespace WebJobs.Script.Host.Standalone
{
    // This needs to move out of this project. Should be plugged into the logging pipeline. But for now, anything that works is fine.
    public class SqlLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new SqlLogger(categoryName);
        }

        public void Dispose()
        {
            // DO NOTHING (Needed because ILoggerProvider : IDisposable)
        }
    }

    /**
     * Sql based implementation of ILogger.
     *
     * Expected table definition:
     *
     * CREATE TABLE [function].[logs]
     * (
     * [Id] [int] IDENTITY(1,1) PRIMARY KEY,
     * [Timestamp] [datetime2](7) NOT NULL,
     * [AppName] [nvarchar](max) NOT NULL,
     * [FunctionName] [nvarchar](max), -- NULL is OK
     * [Message] [nvarchar](max) NOT NULL
     * )
     *
     */
    public class SqlLogger : BufferedLogger
    {
        public const string ConnectionStringName = "AzureWebJobsSqlTracer";
        private const string InsertStatement = "INSERT INTO [function].[Logs] ([Timestamp], [ServerName], [AppName], [FunctionName], [TraceLevel], [Message]) values(@Timestamp, @ServerName, @AppName, @FunctionName, @TraceLevel, @Message)";
        private string _functionApp;

        public SqlLogger(string category)
            : base(category)
        {
            _functionApp = ScriptSettingsManager.Instance.GetSetting(EnvironmentSettingNames.AzureWebsiteName);
        }

        protected async override Task FlushAsync(IEnumerable<TraceMessage> traceMessages)
        {
            string conenctionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringName);
            string functionName = GetFunctionName();

            using (SqlConnection connection = new SqlConnection(conenctionString))
            {
                await connection.OpenAsync();

                using (SqlCommand command = new SqlCommand(InsertStatement, connection))
                {
                    command.Parameters.Add("@Timestamp", SqlDbType.DateTime2);
                    command.Parameters.Add("@ServerName", SqlDbType.NVarChar).Value = Environment.MachineName;
                    command.Parameters.Add("@TraceLevel", SqlDbType.Int).Value = 100;
                    command.Parameters.Add("@AppName", SqlDbType.NVarChar).Value = _functionApp ?? (object)DBNull.Value;
                    command.Parameters.Add("@FunctionName", SqlDbType.NVarChar).Value = functionName ?? (object)DBNull.Value;
                    command.Parameters.Add("@Message", SqlDbType.NVarChar);

                    foreach (var traceMessage in traceMessages)
                    {
                        command.Parameters["@Timestamp"].Value = traceMessage.Time;
                        command.Parameters["@Message"].Value = traceMessage.Message;

                        await command.ExecuteNonQueryAsync();
                    }
                }
            }
        }
    }
}
