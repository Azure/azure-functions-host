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
            Services services = TryCreateServices();
            if (services == null)
            {
                return;
            }

            Bind<CloudStorageAccount>().ToConstant(services.Account);
            Bind<IHostVersionReader>().ToMethod(() => CreateHostVersionReader(services.Account));
            Bind<IProcessTerminationSignalReader>().To<ProcessTerminationSignalReader>();
            Bind<IProcessTerminationSignalWriter>().To<ProcessTerminationSignalWriter>();
            Bind<IFunctionInstanceLookup>().ToMethod(() => services.GetFunctionInstanceLookup());
            Bind<IFunctionTableLookup>().ToMethod(() => CreateFunctionTable(services.Account));
            Bind<IFunctionTable>().ToMethod(() => CreateFunctionTable(services.Account));
            Bind<IRunningHostTableReader>().ToMethod(() => CreateRunningHostTableReader(services.Account));
            Bind<IFunctionUpdatedLogger>().ToMethod(() => services.GetFunctionUpdatedLogger());
            Bind<AzureTable<FunctionLocation, FunctionStatsEntity>>().ToMethod(() => CreateInvokeStatsTable(services.Account));
            Bind<ICausalityReader>().ToMethod(() => CreateCausalityReader(services));
            Bind<ICloudQueueClient>().ToMethod(() => new SdkCloudStorageAccount(services.Account).CreateCloudQueueClient());
            Bind<ICloudTableClient>().ToMethod(() => new SdkCloudStorageAccount(services.Account).CreateCloudTableClient());
            Bind<IInvoker>().To<Invoker>();
            Bind<IInvocationLogLoader>().To<InvocationLogLoader>();
            Bind<IPersistentQueue<PersistentQueueMessage>>().To<PersistentQueue<PersistentQueueMessage>>();
            Bind<IFunctionInstanceLogger>().ToMethod(() => CreateFunctionInstanceLogger(services));
            Bind<IIndexer>().To<Dashboard.Indexers.Indexer>();
            BindFunctionInvocationIndexReader("invocationsInJobReader", TableNames.FunctionsInJobIndex);
            BindFunctionInvocationIndexReader("invocationsInFunctionReader",
                DashboardTableNames.FunctionInvokeLogIndexMruFunction);
            BindFunctionInvocationIndexReader("recentInvocationsReader", DashboardTableNames.FunctionInvokeLogIndexMru);
            BindFunctionInvocationIndexReader("invocationChildrenReader", TableNames.FunctionCausalityLog);
        }

        void BindFunctionInvocationIndexReader(string argName, string tableName)
        {
            Bind<IFunctionInvocationIndexReader>().To<FunctionInvocationIndexReader>()
                .When(r => r.Target.Name == argName)
                .WithConstructorArgument("tableName", tableName);
        }

        private static Services TryCreateServices()
        {
            // Validate services
            try
            {
                var val = GetRuntimeConnectionString();
                if (val != null)
                {
                    SdkSetupState.ConnectionStringState = SdkSetupState.ConnectionStringStates.Valid;
                    return GetServices(val);
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

        private static ICausalityReader CreateCausalityReader(Services services)
        {
            IAzureTable<TriggerReasonEntity> table = new AzureTable<TriggerReasonEntity>(services.Account, TableNames.FunctionCausalityLog);
            IFunctionInstanceLookup logger = services.GetFunctionInstanceLookup(); // read-mode
            return new CausalityLogger(table, logger);
        }

        private static IFunctionInstanceLogger CreateFunctionInstanceLogger(Services services)
        {
            IAzureTableReader<ExecutionInstanceLogEntity> tableLookup = services.GetFunctionLookupTable();
            var tableStatsSummary = CreateInvokeStatsTable(services.Account);
            var tableMru = CreateIndexTable(services.Account, DashboardTableNames.FunctionInvokeLogIndexMru);
            var tableMruByFunction = CreateIndexTable(services.Account,
                DashboardTableNames.FunctionInvokeLogIndexMruFunction);
            var tableMruByFunctionSucceeded = CreateIndexTable(services.Account,
                DashboardTableNames.FunctionInvokeLogIndexMruFunctionSucceeded);
            var tableMruFunctionFailed = CreateIndexTable(services.Account,
                DashboardTableNames.FunctionInvokeLogIndexMruFunctionFailed);

            return new ExecutionStatsAggregator(
                tableLookup,
                tableStatsSummary,
                tableMru,
                tableMruByFunction,
                tableMruByFunctionSucceeded,
                tableMruFunctionFailed);
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

        // Get a Services object based on current configuration.
        // $$$ Really should just get rid of this object and use DI all the way through. 
        static Services GetServices(string runtimeConnectionString)
        {
            // Antares mode
            var ai = new AccountInfo
            {
                AccountConnectionString = runtimeConnectionString
            };
            return new Services(ai);
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
