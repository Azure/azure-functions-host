using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using AzureTables;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;

namespace Microsoft.WindowsAzure.Jobs
{
    // Despite the name, this is not an IOC container.
    // This provides a global view of the distributed application (service, webpage, logging, tooling, etc)
    // Anything that needs an azure endpoint can go here.
    // This access the raw settings (especially account name) from Secrets, but then also provides the
    // policy and references to stitch everything together.
    internal class Services
    {
        private readonly IAccountInfo _accountInfo;
        private readonly CloudStorageAccount _account;

        public Services(IAccountInfo accountInfo)
        {
            _accountInfo = accountInfo;
            _account = CloudStorageAccount.Parse(accountInfo.AccountConnectionString);
        }

        public CloudStorageAccount Account
        {
            get { return _account; }
        }

        public string AccountConnectionString
        {
            get { return _accountInfo.AccountConnectionString; }
        }

        public IAccountInfo AccountInfo
        {
            get { return _accountInfo; }
        }

        // @@@ Remove this, move to be Ninject based. 
        public IFunctionTable GetFunctionTable()
        {
            IAzureTable<FunctionDefinition> table = new AzureTable<FunctionDefinition>(_account, EndpointNames.FunctionIndexTableName);

            return new FunctionTable(table);
        }

        public IRunningHostTableWriter GetRunningHostTableWriter()
        {
            IAzureTable<RunningHost> table = new AzureTable<RunningHost>(_account, EndpointNames.RunningHostsTableName);

            return new RunningHostTableWriter(table);
        }

        public IRunningHostTableReader GetRunningHostTableReader()
        {
            IAzureTable<RunningHost> table = new AzureTable<RunningHost>(_account, EndpointNames.RunningHostsTableName);

            return new RunningHostTableReader(table);
        }

        // $$$ Returning bundles of interfaces... this is really looking like we need IOC.
        // Similar bundle with FunctionExecutionContext
        public ExecuteFunctionInterfaces GetExecuteFunctionInterfaces()
        {
            var x = GetFunctionUpdatedLogger();

            return new ExecuteFunctionInterfaces
            {
                AccountInfo = _accountInfo,
                Logger = x,
                Lookup = x,
                CausalityLogger = GetCausalityLogger()
            };
        }

        public ICausalityLogger GetCausalityLogger()
        {
            IAzureTable<TriggerReasonEntity> table = new AzureTable<TriggerReasonEntity>(_account, EndpointNames.FunctionCausalityLog);
            IFunctionInstanceLookup logger = null; // write-only mode
            return new CausalityLogger(table, logger);
        }

        public ICausalityReader GetCausalityReader()
        {
            IAzureTable<TriggerReasonEntity> table = new AzureTable<TriggerReasonEntity>(_account, EndpointNames.FunctionCausalityLog);
            IFunctionInstanceLookup logger = this.GetFunctionInstanceLookup(); // read-mode
            return new CausalityLogger(table, logger);
        }

        public FunctionUpdatedLogger GetFunctionUpdatedLogger()
        {
            var table = new AzureTable<ExecutionInstanceLogEntity>(_account, EndpointNames.FunctionInvokeLogTableName);
            return new FunctionUpdatedLogger(table);
        }

        // Streamlined case if we just need to lookup specific function instances.
        // In this case, we don't need all the secondary indices.
        public IFunctionInstanceLookup GetFunctionInstanceLookup()
        {
            IAzureTableReader<ExecutionInstanceLogEntity> tableLookup = GetFunctionLookupTable();
            return new ExecutionStatsAggregator(tableLookup);
        }

        public IFunctionInstanceQuery GetFunctionInstanceQuery()
        {
            return GetStatsAggregatorInternal();
        }

        public IFunctionInstanceLogger GetFunctionInstanceLogger()
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
