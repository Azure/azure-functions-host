using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ninject;
using Ninject.Modules;
using WebFrontEnd.Configuration;
using DaasEndpoints;
using RunnerInterfaces;
using System.Diagnostics;
using Microsoft.WindowsAzure.StorageClient;
using Microsoft.WindowsAzure;

namespace WebFrontEnd
{
    public class AppModule : NinjectModule
    {
        public static StringBuilder _sb = new StringBuilder();

        public override void Load()
        {               
            Services services;

            // Validate services
            {
                try
                {
                    var configService = new ConfigurationService();
                    var appConfig = configService.Current;

                    services = GetServices(configService);
                    services.GetExecutionCompleteQueue(); // verify account is valid


                    Bind<ConfigurationService>().ToConstant(configService);
                    Bind<AppConfiguration>().ToConstant(appConfig);
                    Bind<Services>().ToConstant(services); // $$$ eventually remove this.
                    Bind<IHostVersionReader>().ToConstant(CreateHostVersionReader(services.Account));
                }
                catch (Exception e)
                {
                    // Invalid
                    SimpleBatchStuff.BadInit = true; // $$$ don't user a global flag.                    
                    return;
                }
            }

            

            // $$$ This list should eventually just cover all of Services, and then we can remove services.
            // $$$ We don't want Services() floating around. It's jsut a default factory for producing objects that 
            // bind against azure storage accounts. 
            Bind<IFunctionInstanceLookup>().ToConstant(services.GetFunctionInstanceLookup());

            // @@@
            if (SimpleBatchStuff.Init(this.Kernel))
            {
                return;
            }

            Bind<IFunctionTableLookup>().ToConstant(services.GetFunctionTable());
            Bind<IRunningHostTableReader>().ToConstant(services.GetRunningHostTableReader());

            QueueFunctionType t = services.GetExecutionType();
            if (t == QueueFunctionType.Unknown)
            {
                var qi = services.GetQueueInterfaces();
                var q = services.GetExecutionQueue();
                var qc = new WorkerRoleExecutionClient(q, qi);

                // Running webdashboard in standalone mode. 
                //Bind<IQueueFunction>().ToConstant(new EmptyQueueFunction());
                Bind<IQueueFunction>().ToConstant(qc);
                return;
            }
            Bind<IQueueFunction>().ToConstant(services.GetQueueFunction());
        }

        private static IHostVersionReader CreateHostVersionReader(CloudStorageAccount account)
        {
            CloudBlobClient client = account.CreateCloudBlobClient();
            CloudBlobContainer container = client.GetContainerReference(EndpointNames.VersionContainerName);
            return new HostVersionReader(container);
        }

        // ### Shouldn't need this. But function instance page demands it just to render.
        class EmptyQueueFunction : IQueueFunction
        {
            public Executor.ExecutionInstanceLogEntity Queue(FunctionInvokeRequest instance)
            {
                throw new NotImplementedException();
            }
        }

        // Get a Services object based on current configuration.
        // $$$ Really should just get rid of this object and use DI all the way through. 
        static Services GetServices(ConfigurationService config)
        {
            var val = config.ReadSetting("SimpleBatchLoggingACS");
            if (!string.IsNullOrWhiteSpace(val))
            {
                // Antares mode
                var ai = new AccountInfo
                {
                    AccountConnectionString = val,
                    WebDashboardUri = "illegal2"
                };
                return new Services(ai);
            }


            AccountInfo accountInfo = new AccountInfo 
            {
                 AccountConnectionString = config.ReadSetting("MainStorage"),
                 WebDashboardUri = config.ReadSetting("WebRoleEndpoint")
            };

            // In WebSite configuration, we don't have dashboard URI and we get the account elsewhere.
            if (string.IsNullOrWhiteSpace(accountInfo.AccountConnectionString))
            {
                accountInfo.AccountConnectionString = config.ReadSetting("AzureStorage");
                accountInfo.WebDashboardUri = "illegal"; // have non-null value. 
            }

            return new Services(accountInfo);
        }
    }
}
