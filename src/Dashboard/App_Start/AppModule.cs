using System;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.Jobs.Host.Runners;
using Microsoft.WindowsAzure.Jobs.Host.Storage;
using Microsoft.WindowsAzure.Jobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.StorageClient;
using Ninject.Modules;

namespace Dashboard
{
    public class AppModule : NinjectModule
    {
        public override void Load()
        {
            Services services;

            // Validate services
            {
                try
                {
                    services = GetServices();
                }
                catch (Exception e)
                {
                    // Invalid
                    SimpleBatchStuff.BadInitErrorMessage = e.Message; // $$$ don't use a global flag.                    
                    return;
                }
            }

            Bind<CloudStorageAccount>().ToConstant(services.Account);
            Bind<Services>().ToConstant(services); // $$$ eventually remove this.
            Bind<IHostVersionReader>().ToConstant(CreateHostVersionReader(services.Account));
            Bind<IProcessTerminationSignalReader>().To<ProcessTerminationSignalReader>();
            Bind<IProcessTerminationSignalWriter>().To<ProcessTerminationSignalWriter>();

            // $$$ This list should eventually just cover all of Services, and then we can remove services.
            // $$$ We don't want Services() floating around. It's jsut a default factory for producing objects that 
            // bind against azure storage accounts. 
            Bind<IFunctionInstanceLookup>().ToConstant(services.GetFunctionInstanceLookup());

            Bind<IFunctionTableLookup>().ToConstant(services.GetFunctionTable());
            Bind<IRunningHostTableReader>().ToConstant(services.GetRunningHostTableReader());
            Bind<IFunctionUpdatedLogger>().ToMethod((ignore) => services.GetFunctionUpdatedLogger());
            Bind<ICloudQueueClient>().ToMethod((ignore) => new SdkCloudStorageAccount(services.Account).CreateCloudQueueClient());
            Bind<IInvoker>().To<Invoker>();

            return;
        }

        private static IHostVersionReader CreateHostVersionReader(CloudStorageAccount account)
        {
            CloudBlobClient client = account.CreateCloudBlobClient();
            CloudBlobContainer container = client.GetContainerReference(ContainerNames.VersionContainerName);
            return new HostVersionReader(container);
        }

        // Get a Services object based on current configuration.
        // $$$ Really should just get rid of this object and use DI all the way through. 
        static Services GetServices()
        {
            var val = new DefaultConnectionStringProvider().GetConnectionString(JobHost.LoggingConnectionStringName);

            string validationErrorMessage;
            if (!new DefaultStorageValidator().TryValidateConnectionString(val, out validationErrorMessage))
            {
                throw new InvalidOperationException(validationErrorMessage);
            }

            // Antares mode
            var ai = new AccountInfo
            {
                AccountConnectionString = val,
                WebDashboardUri = "illegal2"
            };
            return new Services(ai);
        }
    }
}
