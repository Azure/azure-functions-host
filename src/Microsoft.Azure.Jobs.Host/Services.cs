using AzureTables;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Runners;
using Microsoft.Azure.Jobs.Host.Storage;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs
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

        public IRunningHostTableWriter GetRunningHostTableWriter()
        {
            IAzureTable<RunningHost> table = new AzureTable<RunningHost>(_account, TableNames.RunningHostsTableName);

            return new RunningHostTableWriter(table);
        }

        // $$$ Returning bundles of interfaces... this is really looking like we need IOC.
        // Similar bundle with FunctionExecutionContext
        public ExecuteFunctionInterfaces GetExecuteFunctionInterfaces()
        {
            return new ExecuteFunctionInterfaces
            {
                AccountInfo = _accountInfo
            };
        }
    }
}
