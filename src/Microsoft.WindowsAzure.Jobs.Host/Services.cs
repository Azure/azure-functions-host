using AzureTables;
using Microsoft.WindowsAzure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Jobs.Host.Runners;
using Microsoft.WindowsAzure.Jobs.Host.Storage;
using Microsoft.WindowsAzure.Storage;

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

        public IAccountInfo AccountInfo
        {
            get { return _accountInfo; }
        }

        public IFunctionsInJobIndexer GetFunctionInJobIndexer()
        {
            if (WebJobRunIdentifier.Current == null)
            {
                return new NullFunctionsInJobIndexer();
            }

            var account = new SdkCloudStorageAccount(_account);
            var client = account.CreateCloudTableClient();
            return new FunctionsInJobIndexer(client, WebJobRunIdentifier.Current);
        }

        public IRunningHostTableWriter GetRunningHostTableWriter()
        {
            IAzureTable<RunningHost> table = new AzureTable<RunningHost>(_account, TableNames.RunningHostsTableName);

            return new RunningHostTableWriter(table);
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

        private ICausalityLogger GetCausalityLogger()
        {
            IAzureTable<TriggerReasonEntity> table = new AzureTable<TriggerReasonEntity>(_account, TableNames.FunctionCausalityLog);
            IFunctionInstanceLookup logger = null; // write-only mode
            return new CausalityLogger(table, logger);
        }

        public FunctionUpdatedLogger GetFunctionUpdatedLogger()
        {
            var table = new AzureTable<ExecutionInstanceLogEntity>(_account, TableNames.FunctionInvokeLogTableName);
            return new FunctionUpdatedLogger(table);
        }

        // Streamlined case if we just need to lookup specific function instances.
        // In this case, we don't need all the secondary indices.
        public IFunctionInstanceLookup GetFunctionInstanceLookup()
        {
            IAzureTableReader<ExecutionInstanceLogEntity> tableLookup = GetFunctionLookupTable();
            return new FunctionInstanceLookup(tableLookup);
        }

        public IAzureTableReader<ExecutionInstanceLogEntity> GetFunctionLookupTable()
        {
            return new AzureTable<ExecutionInstanceLogEntity>(
                  _account,
                  TableNames.FunctionInvokeLogTableName);
        }
    }
}
