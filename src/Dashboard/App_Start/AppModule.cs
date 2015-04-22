// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Web;
using System.Web.Caching;
using System.Web.Mvc;
using Dashboard.Data;
using Dashboard.Data.Logs;
using Dashboard.Filters;
using Dashboard.HostMessaging;
using Dashboard.Indexers;
using Microsoft.Azure.WebJobs.Protocols;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Ninject.Modules;
using Ninject.Web.Mvc.FilterBindingSyntax;

namespace Dashboard
{
    public class AppModule : NinjectModule
    {
        public override void Load()
        {
            DashboardAccountContext context = TryCreateAccount();
            Bind<DashboardAccountContext>().ToConstant(context);

            this.BindFilter<AccountContextAttribute>(FilterScope.Global, 0);

            CloudStorageAccount account = context.StorageAccount;
            if (account == null)
            {
                return;
            }

            CloudBlobClient blobClient = account.CreateCloudBlobClient();
            CloudQueueClient queueClient = account.CreateCloudQueueClient();

            Bind<CloudStorageAccount>().ToConstant(account);
            Bind<CloudBlobClient>().ToConstant(blobClient);
            Bind<CloudQueueClient>().ToConstant(queueClient);

            Bind<IHostVersionReader>().To<HostVersionReader>();
            Bind<IDashboardVersionManager>().To<DashboardVersionManager>();
            Bind<IFunctionInstanceLookup>().To<FunctionInstanceLookup>();
            Bind<IFunctionInstanceLogger>().To<FunctionInstanceLogger>();
            Bind<IHostIndexManager>().To<HostIndexManager>();
            Bind<IFunctionIndexVersionManager>().To<FunctionIndexVersionManager>();
            Bind<IFunctionIndexManager>().To<FunctionIndexManager>();
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
            Bind<IIndexer>().To<UpgradeIndexer>();
            Bind<IInvoker>().To<Invoker>();
            Bind<IAbortRequestLogger>().To<AbortRequestLogger>();
            Bind<IAborter>().To<Aborter>();

            Bind<IIndexerLogWriter>().To<IndexerBlobLogWriter>();
            Bind<IIndexerLogReader>().To<IndexerBlobLogReader>();
        }

        private static DashboardAccountContext TryCreateAccount()
        {
            DashboardAccountContext context = new DashboardAccountContext();

            string connectionString = ConnectionStringProvider.GetConnectionString(
                DashboardAccountContext.ConnectionStringName);
            if (String.IsNullOrEmpty(connectionString))
            {
                context.ConnectionStringState = ConnectionStringState.Missing;
                return context;
            }

            CloudStorageAccount account;
            if (!CloudStorageAccount.TryParse(connectionString, out account))
            {
                context.ConnectionStringState = ConnectionStringState.Unparsable;
                return context;
            }

            context.SdkStorageAccountName = account.Credentials.AccountName;

            if (!StorageAccountValidator.ValidateEndpointsSecure(account))
            {
                context.ConnectionStringState = ConnectionStringState.Insecure;
                return context;
            }

            if (!StorageAccountValidator.ValidateAccountAccessible(account))
            {
                context.ConnectionStringState = ConnectionStringState.Inaccessible;
                return context;
            }

            context.ConnectionStringState = ConnectionStringState.Valid;
            context.StorageAccount = account;
            return context;
        }
    }
}
