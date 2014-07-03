using System;
using System.Web;
using System.Web.Caching;
using Dashboard.Data;
using Dashboard.HostMessaging;
using Dashboard.Indexers;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Host.Runners;
using Microsoft.Azure.Jobs.Protocols;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Ninject.Modules;

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

            CloudBlobClient blobClient = sdkAccount.CreateCloudBlobClient();
            CloudQueueClient queueClient = sdkAccount.CreateCloudQueueClient();
            CloudTableClient tableClient = sdkAccount.CreateCloudTableClient();

            Bind<CloudStorageAccount>().ToConstant(sdkAccount);
            Bind<CloudBlobClient>().ToConstant(blobClient);
            Bind<CloudQueueClient>().ToConstant(queueClient);
            Bind<CloudTableClient>().ToConstant(tableClient);

            Bind<IHostVersionReader>().To<HostVersionReader>();
            Bind<IFunctionInstanceLookup>().To<FunctionInstanceLookup>();
            Bind<IFunctionInstanceLogger>().To<FunctionInstanceLogger>();
            Bind<IHostIndexManager>().To<HostIndexManager>();
            Bind<IFunctionLookup>().To<FunctionLookup>();
            Bind<IFunctionIndexReader>().To<FunctionIndexReader>();
            Bind<IHeartbeatValidityMonitor>().To<HeartbeatValidityMonitor>();
            Bind<IHeartbeatMonitor>().To<HeartbeatMonitor>();
            Bind<Cache>().ToConstant(HttpRuntime.Cache);
            Bind<IFunctionStatisticsReader>().To<FunctionStatisticsReader>();
            Bind<IFunctionStatisticsWriter>().To<FunctionStatisticsWriter>();
            Bind<IRecentInvocationIndexReader>().To<RecentInvocationIndexReader>();
            Bind<IRecentInvocationIndexWriter>().To<RecentInvocationIndexWriter>();
            Bind<IRecentInvocationIndexByFunctionReader>().To<RecentInvocationIndexByFunctionReader>();
            Bind<IRecentInvocationIndexByFunctionWriter>().To<RecentInvocationIndexByFunctionWriter>();
            Bind<IRecentInvocationIndexByJobRunReader>().To<RecentInvocationIndexByJobRunReader>();
            Bind<IRecentInvocationIndexByJobRunWriter>().To<RecentInvocationIndexByJobRunWriter>();
            Bind<IRecentInvocationIndexByParentReader>().To<RecentInvocationIndexByParentReader>();
            Bind<IRecentInvocationIndexByParentWriter>().To<RecentInvocationIndexByParentWriter>();
            Bind<IHostMessageSender>().To<HostMessageSender>();
            Bind<IPersistentQueueReader<PersistentQueueMessage>>().To<PersistentQueueReader<PersistentQueueMessage>>();
            Bind<IFunctionQueuedLogger>().To<FunctionInstanceLogger>();
            Bind<IHostIndexer>().To<HostIndexer>();
            Bind<IFunctionIndexer>().To<FunctionIndexer>();
            Bind<IIndexer>().To<Indexer>();
            Bind<IInvoker>().To<Invoker>();
            Bind<IAbortRequestLogger>().To<AbortRequestLogger>();
            Bind<IAborter>().To<Aborter>();
        }

        private static CloudStorageAccount TryCreateAccount()
        {
            // Validate services
            try
            {
                var val = GetDashboardConnectionString();
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

        private static string GetDashboardConnectionString()
        {
            var val = new AmbientConnectionStringProvider().GetConnectionString("Dashboard");

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
}
