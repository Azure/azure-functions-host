using System;
using System.Collections.Generic;
using System.IO;
using Executor;
using Microsoft.WindowsAzure.StorageClient;
using Orchestrator;
using RunnerInterfaces;
using System.Linq;
using Newtonsoft.Json;
using System.Threading;
using AzureTables;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure;
using SimpleBatch;

namespace DaasEndpoints
{
    // Services related to logging
    public partial class Services
    {
        public ICausalityLogger GetCausalityLogger()
        {
            IAzureTable<TriggerReasonEntity> table = new AzureTable<TriggerReasonEntity>(_account, EndpointNames.FunctionCausalityLog);
            IFunctionInstanceLookup logger = null; // write-only mode
            return new CausalityLogger(table, logger);
        }

        public ICausalityReader GetCausalityReader()
        {
            IAzureTable<TriggerReasonEntity> table = new AzureTable<TriggerReasonEntity>(_account, EndpointNames.FunctionCausalityLog);
            IFunctionInstanceLookup logger = this.GetFunctionInvokeLookup(); // read-mode
            return new CausalityLogger(table, logger);
        }

        public IFunctionUpdatedLogger GetFunctionInvokeLogger()
        {
            return new FunctionInvokeLogger
            {
                Account = _account,
                TableName = EndpointNames.FunctionInvokeLogTableName,
            };
        }

        // Streamlined case if we just need to lookup specific function instances.
        // In this case, we don't need all the secondary indices.
        public IFunctionInstanceLookup GetFunctionInvokeLookup()
        {
            IAzureTableReader<ExecutionInstanceLogEntity> tableLookup = GetFunctionLookupTable();
            return new ExecutionStatsAggregator(tableLookup);
        }

        // Used by Executors to notify of completed functions
        // Will send a message to orchestrator to aggregate stats together.
        public ExecutionStatsAggregatorBridge GetStatsAggregatorBridge()
        {
            var queue = this.GetExecutionCompleteQueue();
            return new ExecutionStatsAggregatorBridge(queue);
        }

        public IFunctionInstanceQuery GetFunctionInvokeQuery()
        {
            return GetStatsAggregatorInternal();
        }

        // Actually does the aggregation. Receives a message from the bridge.
        public IFunctionCompleteLogger GetStatsAggregator()
        {
            return GetStatsAggregatorInternal();
        }

        private ExecutionStatsAggregator GetStatsAggregatorInternal()
        {
            IAzureTableReader<ExecutionInstanceLogEntity> tableLookup = GetFunctionLookupTable();
            var tableStatsSummary = GetInvokeStatsTable();
            var tableMru = GetIndexTable(EndpointNames.FunctionInvokeLogIndexMru);
            var tableMruByFunction = GetIndexTable(EndpointNames.FunctionInvokeLogIndexMruFunction);
            var tableMruByFunctionSucceeded = GetIndexTable(EndpointNames.FunctionInvokeLogIndexMruFunctionSucceeded);
            var tableMruFunctionFailed = GetIndexTable(EndpointNames.FunctionInvokeLogIndexMruFunctionFailed);

            return new ExecutionStatsAggregator(
                tableLookup,
                tableStatsSummary,
                tableMru,
                tableMruByFunction,
                tableMruByFunctionSucceeded,
                tableMruFunctionFailed);
        }

        // Table that maps function types to summary statistics. 
        // Table is populated by the ExecutionStatsAggregator
        public AzureTable<FunctionLocation, FunctionStatsEntity> GetInvokeStatsTable()
        {
            return new AzureTable<FunctionLocation, FunctionStatsEntity>(
                _account,
                EndpointNames.FunctionInvokeStatsTableName,
                 row => Tuple.Create("1", row.ToString()));
        }

        private IAzureTable<FunctionInstanceGuid> GetIndexTable(string tableName)
        {
            return new AzureTable<FunctionInstanceGuid>(_account, tableName);
        }

        private IAzureTableReader<ExecutionInstanceLogEntity> GetFunctionLookupTable()
        {
            return new AzureTable<ExecutionInstanceLogEntity>(
                  _account,
                  EndpointNames.FunctionInvokeLogTableName);
        }
    }
}