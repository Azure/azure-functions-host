using System;
using AzureTables;
using Dashboard.Data;
using Dashboard.Indexers;
using Dashboard.Protocols;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Runners;
using Microsoft.Azure.Jobs.Host.Storage;
using Microsoft.Azure.Jobs.Host.Storage.Queue;
using Microsoft.Azure.Jobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Ninject.Modules;
using Ninject.Syntax;

namespace Dashboard
{
    public class AppModule : NinjectModule
    {
        public override void Load()
        {
            CloudStorageAccount account = TryCreateAccount();
            if (account == null)
            {
                return;
            }

            Bind<CloudStorageAccount>().ToConstant(account);
            Bind<IHostVersionReader>().ToMethod(() => CreateHostVersionReader(account));
            Bind<IProcessTerminationSignalReader>().To<ProcessTerminationSignalReader>();
            Bind<IProcessTerminationSignalWriter>().To<ProcessTerminationSignalWriter>();
            Bind<IFunctionInstanceLookup>().ToMethod(() => CreateFunctionInstanceLookup(account));
            Bind<IFunctionTableLookup>().ToMethod(() => CreateFunctionTable(account));
            Bind<IFunctionTable>().ToMethod(() => CreateFunctionTable(account));
            Bind<IRunningHostTableReader>().ToMethod(() => CreateRunningHostTableReader(account));
            Bind<AzureTable<FunctionLocation, FunctionStatsEntity>>().ToMethod(() => CreateInvokeStatsTable(account));
            Bind<ICausalityReader>().ToMethod(() => CreateCausalityReader(account));
            Bind<ICausalityLogger>().ToMethod(() => CreateCausalityLogger(account));
            Bind<ICloudQueueClient>().ToMethod(() => new SdkCloudStorageAccount(account).CreateCloudQueueClient());
            Bind<ICloudTableClient>().ToMethod(() => new SdkCloudStorageAccount(account).CreateCloudTableClient());
            Bind<IInvoker>().To<Invoker>();
            Bind<IInvocationLogLoader>().To<InvocationLogLoader>();
            Bind<IPersistentQueue<PersistentQueueMessage>>().To<PersistentQueue<PersistentQueueMessage>>();
            Bind<IFunctionInstanceLogger>().ToMethod(() => CreateFunctionInstanceLogger(account));
            Bind<IFunctionQueuedLogger>().To<FunctionInstanceLogger>();
            Bind<IIndexer>().To<Dashboard.Indexers.Indexer>();
            Bind<IFunctionsInJobIndexer>().To<FunctionsInJobIndexer>();
            BindFunctionInvocationIndexReader("invocationsInJobReader", DashboardTableNames.FunctionsInJobIndex);
            BindFunctionInvocationIndexReader("invocationsInFunctionReader",
                DashboardTableNames.FunctionInvokeLogIndexMruFunction);
            BindFunctionInvocationIndexReader("recentInvocationsReader", DashboardTableNames.FunctionInvokeLogIndexMru);
            BindFunctionInvocationIndexReader("invocationChildrenReader", DashboardTableNames.FunctionCausalityLog);
        }

        void BindFunctionInvocationIndexReader(string argName, string tableName)
        {
            Bind<IFunctionInvocationIndexReader>().To<FunctionInvocationIndexReader>()
                .When(r => r.Target.Name == argName)
                .WithConstructorArgument("tableName", tableName);
        }

        private static CloudStorageAccount TryCreateAccount()
        {
            // Validate services
            try
            {
                var val = GetRuntimeConnectionString();
                if (val != null)
                {
                    SdkSetupState.ConnectionStringState = SdkSetupState.ConnectionStringStates.Valid;
                    return CloudStorageAccount.Parse(val);
                }
                SdkSetupState.ConnectionStringState = SdkSetupState.ConnectionStringStates.Missing;
            }
            catch (Exception e)
            {
                // Invalid
                SdkSetupState.ConnectionStringState = SdkSetupState.ConnectionStringStates.Invalid;
                SdkSetupState.BadInitErrorMessage = e.Message; // $$$ don't use a global flag.                    
            }
            return null;
        }

        private static IAzureTable<TriggerReasonEntity> CreateCausalityTable(CloudStorageAccount account)
        {
            return new AzureTable<TriggerReasonEntity>(account, DashboardTableNames.FunctionCausalityLog);
        }

        private static ICausalityReader CreateCausalityReader(CloudStorageAccount account)
        {
            IAzureTable<TriggerReasonEntity> table = CreateCausalityTable(account);
            IFunctionInstanceLookup logger = CreateFunctionInstanceLookup(account); // read-mode
            return new CausalityLogger(table, logger);
        }

        private static ICausalityLogger CreateCausalityLogger(CloudStorageAccount account)
        {
            IAzureTable<TriggerReasonEntity> table = CreateCausalityTable(account);
            IFunctionInstanceLookup logger = null; // write-only mode
            return new CausalityLogger(table, logger);
        }

        private static IFunctionInstanceLogger CreateFunctionInstanceLogger(CloudStorageAccount account)
        {
            IFunctionInstanceLogger instanceLogger = new FunctionInstanceLogger(new SdkCloudStorageAccount(account).CreateCloudTableClient());

            IAzureTableReader<ExecutionInstanceLogEntity> tableLookup = CreateFunctionLookupTable(account);
            var tableStatsSummary = CreateInvokeStatsTable(account);
            var tableMru = CreateIndexTable(account, DashboardTableNames.FunctionInvokeLogIndexMru);
            var tableMruByFunction = CreateIndexTable(account,
                DashboardTableNames.FunctionInvokeLogIndexMruFunction);
            var tableMruByFunctionSucceeded = CreateIndexTable(account,
                DashboardTableNames.FunctionInvokeLogIndexMruFunctionSucceeded);
            var tableMruFunctionFailed = CreateIndexTable(account,
                DashboardTableNames.FunctionInvokeLogIndexMruFunctionFailed);

            IFunctionInstanceLogger statsAggregator = new ExecutionStatsAggregator(
                tableLookup,
                tableStatsSummary,
                tableMru,
                tableMruByFunction,
                tableMruByFunctionSucceeded,
                tableMruFunctionFailed);

            return new CompositeFunctionInstanceLogger(instanceLogger, statsAggregator);
        }


        // Streamlined case if we just need to lookup specific function instances.
        // In this case, we don't need all the secondary indices.
        private static IFunctionInstanceLookup CreateFunctionInstanceLookup(CloudStorageAccount account)
        {
            IAzureTableReader<ExecutionInstanceLogEntity> tableLookup = CreateFunctionLookupTable(account);
            return new FunctionInstanceLookup(tableLookup);
        }

        private static IAzureTableReader<ExecutionInstanceLogEntity> CreateFunctionLookupTable(CloudStorageAccount account)
        {
            return new AzureTable<ExecutionInstanceLogEntity>(account, DashboardTableNames.FunctionInvokeLogTableName);
        }


        private static IFunctionTable CreateFunctionTable(CloudStorageAccount account)
        {
            IAzureTable<FunctionDefinition> table = new AzureTable<FunctionDefinition>(account,
                DashboardTableNames.FunctionIndexTableName);

            return new FunctionTable(table);
        }

        private static IHostVersionReader CreateHostVersionReader(CloudStorageAccount account)
        {
            CloudBlobClient client = account.CreateCloudBlobClient();
            CloudBlobContainer container = client.GetContainerReference(ContainerNames.VersionContainerName);
            return new HostVersionReader(container);
        }

        private static IAzureTable<FunctionInstanceGuid> CreateIndexTable(CloudStorageAccount account, string tableName)
        {
            return new AzureTable<FunctionInstanceGuid>(account, tableName);
        }

        // Table that maps function types to summary statistics. 
        // Table is populated by the ExecutionStatsAggregator
        private static AzureTable<FunctionLocation, FunctionStatsEntity> CreateInvokeStatsTable(CloudStorageAccount account)
        {
            return new AzureTable<FunctionLocation, FunctionStatsEntity>(
                account,
                DashboardTableNames.FunctionInvokeStatsTableName,
                 row => Tuple.Create("1", row.ToString()));
        }

        private static IRunningHostTableReader CreateRunningHostTableReader(CloudStorageAccount account)
        {
            IAzureTable<RunningHost> table = new AzureTable<RunningHost>(account, TableNames.RunningHostsTableName);

            return new RunningHostTableReader(table);
        }

        private static string GetRuntimeConnectionString()
        {
            var val = new AmbientConnectionStringProvider().GetConnectionString(JobHost.LoggingConnectionStringName);

            if (String.IsNullOrEmpty(val))
            {
                return null;
            }

            string validationErrorMessage;
            if (!new DefaultStorageValidator().TryValidateConnectionString(val, out validationErrorMessage))
            {
                throw new InvalidOperationException(validationErrorMessage);
            }
            return val;
        }
    }

    internal static class NinjectBindingExtensions
    {
        public static IBindingWhenInNamedWithOrOnSyntax<T> ToMethod<T>(this IBindingToSyntax<T> binding, Func<T> factoryMethod)
        {
            return binding.ToMethod(_ => factoryMethod());
        }
    }
}
