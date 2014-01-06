using System;
using Dashboard.Configuration;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.StorageClient;
using Ninject.Modules;

namespace Dashboard
{
    public class AppModule : NinjectModule
    {
        public override void Load()
        {
            var configService = new ConfigurationService();
            var appConfig = configService.Current;

            Services services;

            // Validate services
            {
                try
                {
                    services = GetServices(configService);
                }
                catch (Exception e)
                {
                    // Invalid
                    SimpleBatchStuff.BadInitErrorMessage = e.Message; // $$$ don't use a global flag.                    
                    return;
                }
            }

            Bind<ConfigurationService>().ToConstant(configService);
            Bind<AppConfiguration>().ToConstant(appConfig);
            Bind<Services>().ToConstant(services); // $$$ eventually remove this.
            Bind<IHostVersionReader>().ToConstant(CreateHostVersionReader(services.Account));
            Bind<IProcessTerminationSignalReader>().ToConstant(CreateProcessTerminationSignalReader(services.Account));
            Bind<IProcessTerminationSignalWriter>().ToConstant(CreateProcessTerminationSignalWriter(services.Account));

            // $$$ This list should eventually just cover all of Services, and then we can remove services.
            // $$$ We don't want Services() floating around. It's jsut a default factory for producing objects that 
            // bind against azure storage accounts. 
            Bind<IFunctionInstanceLookup>().ToConstant(services.GetFunctionInstanceLookup());

            Bind<IFunctionTableLookup>().ToConstant(services.GetFunctionTable());
            Bind<IRunningHostTableReader>().ToConstant(services.GetRunningHostTableReader());

            return;
        }

        private static IHostVersionReader CreateHostVersionReader(CloudStorageAccount account)
        {
            CloudBlobClient client = account.CreateCloudBlobClient();
            CloudBlobContainer container = client.GetContainerReference(EndpointNames.VersionContainerName);
            return new HostVersionReader(container);
        }

        private static IProcessTerminationSignalReader CreateProcessTerminationSignalReader(CloudStorageAccount account)
        {
            return new ProcessTerminationSignalReader(account);
        }

        private static IProcessTerminationSignalWriter CreateProcessTerminationSignalWriter(CloudStorageAccount account)
        {
            return new ProcessTerminationSignalWriter(account);
        }

        // Get a Services object based on current configuration.
        // $$$ Really should just get rid of this object and use DI all the way through. 
        static Services GetServices(ConfigurationService config)
        {
            var val = config.ReadSetting("SimpleBatchLoggingACS");

            Utility.ValidateConnectionString(val);


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
