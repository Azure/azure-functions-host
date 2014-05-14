using System;
using AzureTables;
using Dashboard.Data;
using Dashboard.Indexers;
using Dashboard.InvocationLog;
using Dashboard.Protocols;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Host.Runners;
using Microsoft.Azure.Jobs.Host.Storage;
using Microsoft.Azure.Jobs.Host.Storage.Queue;
using Microsoft.Azure.Jobs.Host.Storage.Table;
using Microsoft.Azure.Jobs.Protocols;
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
            CloudStorageAccount sdkAccount = TryCreateAccount();
            if (sdkAccount == null)
            {
                return;
            }

            ICloudStorageAccount account = new SdkCloudStorageAccount(sdkAccount);
            ICloudQueueClient queueClient = account.CreateCloudQueueClient();
            ICloudTableClient tableClient = account.CreateCloudTableClient();

            Bind<CloudStorageAccount>().ToConstant(sdkAccount);
            Bind<ICloudQueueClient>().ToConstant(queueClient);
            Bind<ICloudTableClient>().ToConstant(tableClient);
            Bind<IHostVersionReader>().To<HostVersionReader>();
            Bind<IProcessTerminationSignalReader>().To<ProcessTerminationSignalReader>();
            Bind<IProcessTerminationSignalWriter>().To<ProcessTerminationSignalWriter>();
            Bind<IFunctionInstanceLookup>().To<FunctionInstanceLookup>();
            Bind<IFunctionTableLookup>().ToMethod(() => CreateFunctionTable(sdkAccount));
            Bind<IFunctionTable>().ToMethod(() => CreateFunctionTable(sdkAccount));
            Bind<IRunningHostTableReader>().To<RunningHostTableReader>();
            Bind<AzureTable<FunctionLocation, FunctionStatsEntity>>().ToMethod(() => CreateInvokeStatsTable(sdkAccount));
            Bind<ICausalityReader>().ToMethod(() => CreateCausalityReader(tableClient, sdkAccount));
            Bind<ICausalityLogger>().ToMethod(() => CreateCausalityLogger(sdkAccount));
            Bind<IInvoker>().To<Invoker>();
            Bind<IInvocationLogLoader>().To<InvocationLogLoader>();
            Bind<IPersistentQueue<PersistentQueueMessage>>().To<PersistentQueue<PersistentQueueMessage>>();
            Bind<IFunctionInstanceLogger>().ToMethod(() => CreateFunctionInstanceLogger(tableClient, sdkAccount));
            Bind<IFunctionQueuedLogger>().To<FunctionInstanceLogger>();
            Bind<IIndexer>().To<Dashboard.Indexers.Indexer>();
            Bind<IFunctionsInJobIndexer>().To<FunctionsInJobIndexer>();
            BindFunctionInvocationIndexReader("invocationsInJobReader", DashboardTableNames.FunctionsInJobIndex);
            BindFunctionInvocationIndexReader("invocationsInFunctionReader",
                DashboardTableNames.FunctionInvokeLogIndexMruFunction);
            BindFunctionInvocationIndexReader("recentInvocationsReader", DashboardTableNames.FunctionInvokeLogIndexMru);
            BindFunctionInvocationIndexReader("invocationChildrenReader", DashboardTableNames.FunctionCausalityLog);
        }

        private void BindFunctionInvocationIndexReader(string argName, string tableName)
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

        private static ICausalityReader CreateCausalityReader(ICloudTableClient tableClient, CloudStorageAccount account)
        {
            IAzureTable<TriggerReasonEntity> table = CreateCausalityTable(account);
            IFunctionInstanceLookup lookup = new FunctionInstanceLookup(tableClient);
            return new CausalityLogger(table, lookup);
        }

        private static ICausalityLogger CreateCausalityLogger(CloudStorageAccount account)
        {
            IAzureTable<TriggerReasonEntity> table = CreateCausalityTable(account);
            IFunctionInstanceLookup logger = null; // write-only mode
            return new CausalityLogger(table, logger);
        }

        private static IFunctionInstanceLogger CreateFunctionInstanceLogger(ICloudTableClient tableClient, CloudStorageAccount account)
        {
            IFunctionInstanceLogger instanceLogger = new FunctionInstanceLogger(tableClient);
            IFunctionInstanceLookup instanceLookup = new FunctionInstanceLookup(tableClient);

            var tableStatsSummary = new AzureTable<FunctionStatsEntity>(account, DashboardTableNames.FunctionInvokeStatsTableName);
            var tableMru = CreateIndexTable(account, DashboardTableNames.FunctionInvokeLogIndexMru);
            var tableMruByFunction = CreateIndexTable(account,
                DashboardTableNames.FunctionInvokeLogIndexMruFunction);
            var tableMruByFunctionSucceeded = CreateIndexTable(account,
                DashboardTableNames.FunctionInvokeLogIndexMruFunctionSucceeded);
            var tableMruFunctionFailed = CreateIndexTable(account,
                DashboardTableNames.FunctionInvokeLogIndexMruFunctionFailed);

            IFunctionInstanceLogger statsAggregator = new ExecutionStatsAggregator(
                instanceLookup,
                tableStatsSummary,
                tableMru,
                tableMruByFunction,
                tableMruByFunctionSucceeded,
                tableMruFunctionFailed);

            return new CompositeFunctionInstanceLogger(instanceLogger, statsAggregator);
        }

        private static IFunctionTable CreateFunctionTable(CloudStorageAccount account)
        {
            IAzureTable<FunctionDefinition> table = new AzureTable<FunctionDefinition>(account,
                DashboardTableNames.FunctionIndexTableName);

            return new FunctionTable(table);
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
