// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Web;
using System.Web.Caching;
using Dashboard.Data;
using Dashboard.Data.Logs;
using Dashboard.HostMessaging;
using Dashboard.Indexers;
using Dashboard.Infrastructure;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Host.Executors;
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
            Bind<IFunctionIndexVersionManager>().To<FunctionIndexVersionManager>();
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

            Bind<IIndexerLogWriter>().To<IndexerBlobLogWriter>();
            Bind<IIndexerLogReader>().To<IndexerBlobLogReader>();
        }

        private static CloudStorageAccount TryCreateAccount()
        {
            try
            {
                var account = GetDashboardAccount();
                if (account != null)
                {
                    SdkSetupState.ConnectionStringState = SdkSetupState.ConnectionStringStates.Valid;
                    return account;
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

        private static CloudStorageAccount GetDashboardAccount()
        {
            DefaultStorageAccountProvider provider = new DefaultStorageAccountProvider();
            // Explicitly set a value to allow nulls/empty without throwing.
            provider.DashboardConnectionString = provider.DashboardConnectionString;
            CloudStorageAccount account = provider.GetAccount(ConnectionStringNames.Dashboard);
            if (account != null)
            {
                DefaultStorageCredentialsValidator validator = new DefaultStorageCredentialsValidator();
                AspNetTaskExecutor.Execute(() => validator.ValidateCredentialsAsync(account, CancellationToken.None));
            }
            return account;
        }
    }
}
