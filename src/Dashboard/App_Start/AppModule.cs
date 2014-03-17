using System;
using AzureTables;
using Dashboard.Indexers;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Jobs.Host.Storage;
using Microsoft.WindowsAzure.Jobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.Jobs.Host.Storage.Table;
using Microsoft.WindowsAzure.StorageClient;
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
            Bind<Services>().ToConstant(services); // $$$ eventually remove this.
            Bind<IHostVersionReader>().ToMethod(() => CreateHostVersionReader(services.Account));
            Bind<IProcessTerminationSignalReader>().To<ProcessTerminationSignalReader>();
            Bind<IProcessTerminationSignalWriter>().To<ProcessTerminationSignalWriter>();

            // $$$ This list should eventually just cover all of Services, and then we can remove services.
            // $$$ We don't want Services() floating around. It's jsut a default factory for producing objects that 
            // bind against azure storage accounts. 
            Bind<IFunctionInstanceLookup>().ToMethod(() => services.GetFunctionInstanceLookup());
            Bind<IFunctionTableLookup>().ToMethod(() => services.GetFunctionTable());
            Bind<IFunctionTable>().ToMethod(() => services.GetFunctionTable());
            Bind<IRunningHostTableReader>().ToMethod(() => services.GetRunningHostTableReader());
            Bind<IFunctionUpdatedLogger>().ToMethod(() => services.GetFunctionUpdatedLogger());
            Bind<AzureTable<FunctionLocation, FunctionStatsEntity>>().ToMethod(() => services.GetInvokeStatsTable());
            Bind<ICausalityReader>().ToMethod(() => services.GetCausalityReader());
            Bind<ICloudQueueClient>().ToMethod(() => new SdkCloudStorageAccount(services.Account).CreateCloudQueueClient());
            Bind<ICloudTableClient>().ToMethod(() => new SdkCloudStorageAccount(services.Account).CreateCloudTableClient());
            Bind<IInvoker>().To<Invoker>();
            Bind<IInvocationLogLoader>().To<InvocationLogLoader>();
            Bind<IPersistentQueue<PersistentQueueMessage>>().To<PersistentQueue<PersistentQueueMessage>>();
            Bind<IFunctionInstanceLogger>().ToMethod(() => services.GetFunctionInstanceLogger());
            Bind<IIndexer>().To<Dashboard.Indexers.Indexer>();
            BindFunctionInvocationIndexReader("invocationsInJobReader", TableNames.FunctionsInJobIndex);
            BindFunctionInvocationIndexReader("invocationsInFunctionReader", TableNames.FunctionInvokeLogIndexMruFunction);
            BindFunctionInvocationIndexReader("recentInvocationsReader", TableNames.FunctionInvokeLogIndexMru);
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

        private static IHostVersionReader CreateHostVersionReader(CloudStorageAccount account)
        {
            CloudBlobClient client = account.CreateCloudBlobClient();
            CloudBlobContainer container = client.GetContainerReference(ContainerNames.VersionContainerName);
            return new HostVersionReader(container);
        }

        // Get a Services object based on current configuration.
        // $$$ Really should just get rid of this object and use DI all the way through. 
        static Services GetServices(string runtimeConnectionString)
        {
            // Antares mode
            var ai = new AccountInfo
            {
                AccountConnectionString = runtimeConnectionString,
                WebDashboardUri = "illegal2"
            };
            return new Services(ai);
        }

        private static string GetRuntimeConnectionString()
        {
            var val = new DefaultConnectionStringProvider().GetConnectionString(JobHost.LoggingConnectionStringName);

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
