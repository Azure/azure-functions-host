// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Configuration;
using System.Web;
using System.Web.Caching;
using System.Web.Mvc;
using Autofac;
using Autofac.Integration.Mvc;
using Dashboard.Data;
using Dashboard.Data.Logs;
using Dashboard.Filters;
using Dashboard.HostMessaging;
using Dashboard.Indexers;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Protocols;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;

namespace Dashboard
{
    public static class AppModule
    {
        // Optional Appsetting to explicitly set the table name to use with fast logging. 
        private const string FunctionLogTableAppSettingName = "AzureWebJobsLogTableName";

        // Optional Appsetting to control site extension versioning. We can infer log mode from this. 
        private const string FunctionExtensionVersionAppSettingName = "FUNCTIONS_EXTENSION_VERSION";
        private const string FunctionExtensionVersionDisabled = "disabled";

        public static void Load(ContainerBuilder builder)
        {
            DashboardAccountContext context = TryCreateAccount();
            builder.RegisterInstance(context);

            // http://stackoverflow.com/a/21535799/534514
            builder.RegisterType<AccountContextAttribute>().AsResultFilterFor<Controller>().InstancePerRequest();
            builder.RegisterType<HandleErrorAttribute>().AsExceptionFilterFor<Controller>();
            builder.RegisterFilterProvider();

            CloudStorageAccount account = context.StorageAccount;
            if (account == null)
            {
                return;
            }

            builder.RegisterInstance(account).As<CloudStorageAccount>();
            builder.RegisterInstance(account.CreateCloudBlobClient()).As<CloudBlobClient>();

            CloudTableClient tableClient = account.CreateCloudTableClient();

            var tableProvider = GetNewLoggerTableProvider(tableClient);

            if (tableProvider != null)
            {
                context.DisableInvoke = true;

                // fast table reader.                 
                var reader = LogFactory.NewReader(tableProvider);
                builder.RegisterInstance(reader).As<ILogReader>();

                var s = new FastTableReader(reader);

                builder.RegisterInstance(s)
                    .As<IRecentInvocationIndexReader>()
                    .As<IRecentInvocationIndexByFunctionReader>()
                    .As<IFunctionIndexReader>()
                    .As<IFunctionStatisticsReader>()
                    .As<IFunctionInstanceLookup>()
                    .As<IFunctionLookup>().SingleInstance();

                builder.RegisterType<NullHostVersionReader>().As<IHostVersionReader>().SingleInstance();

                // See services used by FunctionsController
                builder.RegisterType<NullInvocationIndexReader>().As<IRecentInvocationIndexByJobRunReader>().SingleInstance();
                builder.RegisterType<NullInvocationIndexReader>().As<IRecentInvocationIndexByParentReader>().SingleInstance();

                builder.RegisterType<NullHeartbeatValidityMonitor>().As<IHeartbeatValidityMonitor>().SingleInstance();
                builder.RegisterType<NullAborter>().As<IAborter>().SingleInstance();
                builder.RegisterType<NullInvoker>().As<IInvoker>().SingleInstance();

                // for diagnostics
                builder.RegisterType<NullIIndexerLogReader>().As<IIndexerLogReader>().SingleInstance();
                builder.RegisterType<NullLogReader>().As<IPersistentQueueReader<PersistentQueueMessage>>().SingleInstance();
                builder.RegisterType<NullIDashboardVersionManager>().As<IDashboardVersionManager>().SingleInstance();
            }
            else
            {
                builder.RegisterInstance(new NullFastReader()).As<ILogReader>();

                // Traditional SDK reader. 

                CloudQueueClient queueClient = account.CreateCloudQueueClient();

                builder.RegisterInstance(queueClient).As<CloudQueueClient>();
                builder.RegisterType<HostVersionReader>().As<IHostVersionReader>().SingleInstance();
                builder.RegisterType<DashboardVersionManager>().As<IDashboardVersionManager>().SingleInstance();
                builder.RegisterType<FunctionInstanceLookup>().As<IFunctionInstanceLookup>().SingleInstance();
                builder.RegisterType<FunctionInstanceLogger>().As<IFunctionInstanceLogger>().SingleInstance();
                builder.RegisterType<HostIndexManager>().As<IHostIndexManager>().SingleInstance();
                builder.RegisterType<FunctionIndexVersionManager>().As<IFunctionIndexVersionManager>().SingleInstance();
                builder.RegisterType<FunctionIndexManager>().As<IFunctionIndexManager>().SingleInstance();
                builder.RegisterType<FunctionLookup>().As<IFunctionLookup>().SingleInstance();
                builder.RegisterType<FunctionIndexReader>().As<IFunctionIndexReader>().SingleInstance();
                builder.RegisterType<HeartbeatValidityMonitor>().As<IHeartbeatValidityMonitor>().SingleInstance();
                builder.RegisterType<HeartbeatMonitor>().As<IHeartbeatMonitor>().SingleInstance();
                builder.RegisterInstance(HttpRuntime.Cache).As<Cache>();
                builder.RegisterType<FunctionStatisticsReader>().As<IFunctionStatisticsReader>().SingleInstance();
                builder.RegisterType<FunctionStatisticsWriter>().As<IFunctionStatisticsWriter>().SingleInstance();
                builder.RegisterType<RecentInvocationIndexReader>().As<IRecentInvocationIndexReader>().SingleInstance();
                builder.RegisterType<RecentInvocationIndexWriter>().As<IRecentInvocationIndexWriter>().SingleInstance();
                builder.RegisterType<RecentInvocationIndexByFunctionReader>().As<IRecentInvocationIndexByFunctionReader>().SingleInstance();
                builder.RegisterType<RecentInvocationIndexByFunctionWriter>().As<IRecentInvocationIndexByFunctionWriter>().SingleInstance();
                builder.RegisterType<RecentInvocationIndexByJobRunReader>().As<IRecentInvocationIndexByJobRunReader>().SingleInstance();
                builder.RegisterType<RecentInvocationIndexByJobRunWriter>().As<IRecentInvocationIndexByJobRunWriter>().SingleInstance();
                builder.RegisterType<RecentInvocationIndexByParentReader>().As<IRecentInvocationIndexByParentReader>().SingleInstance();
                builder.RegisterType<RecentInvocationIndexByParentWriter>().As<IRecentInvocationIndexByParentWriter>().SingleInstance();
                builder.RegisterType<HostMessageSender>().As<IHostMessageSender>().SingleInstance();
                builder.RegisterType<PersistentQueueReader<PersistentQueueMessage>>().As<IPersistentQueueReader<PersistentQueueMessage>>().SingleInstance();
                builder.RegisterType<FunctionInstanceLogger>().As<IFunctionQueuedLogger>().SingleInstance();
                builder.RegisterType<HostIndexer>().As<IHostIndexer>().SingleInstance();
                builder.RegisterType<FunctionIndexer>().As<IFunctionIndexer>().SingleInstance();
                builder.RegisterType<UpgradeIndexer>().As<IIndexer>().SingleInstance();
                builder.RegisterType<Invoker>().As<IInvoker>().SingleInstance();
                builder.RegisterType<AbortRequestLogger>().As<IAbortRequestLogger>().SingleInstance();
                builder.RegisterType<Aborter>().As<IAborter>().SingleInstance();

                builder.RegisterType<IndexerBlobLogWriter>().As<IIndexerLogWriter>().SingleInstance();
                builder.RegisterType<IndexerBlobLogReader>().As<IIndexerLogReader>().SingleInstance();
            }
        }

        // Determine which logging mode. 
        // 1. Fast logging (tables) - return a default provider for the given storage account. 
        // 2. traditional slower logging (blob) - return null. 
        private static ILogTableProvider GetNewLoggerTableProvider(CloudTableClient tableClient)
        {         
            string logTablePrefix = ConfigurationManager.AppSettings[FunctionLogTableAppSettingName];
            if (string.IsNullOrWhiteSpace(logTablePrefix))
            {
                var ver = ConfigurationManager.AppSettings[FunctionExtensionVersionAppSettingName];
                if (string.IsNullOrWhiteSpace(ver))
                {
                    // No Func appsetting, this is using sdk-style older logging. 
                    return null;
                }

                if (string.Equals(ver, FunctionExtensionVersionDisabled, StringComparison.OrdinalIgnoreCase))
                {
                    // Explicitly set to old mode. 
                    return null;
                }

                // This is the common case for Azure Functions. 
                // No prefix, so use the default. 
                var provider = LogFactory.NewLogTableProvider(tableClient);
                return provider;
            }
            else
            {
                // Name is explicitly supplied in an appsetting. Definitely using the fast tables. 
                var provider = LogFactory.NewLogTableProvider(tableClient, logTablePrefix);
                return provider;
            }           
        }

        private static DashboardAccountContext TryCreateAccount()
        {
            DashboardAccountContext context = new DashboardAccountContext();

            string connectionString = ConnectionStringProvider.GetConnectionString(DashboardAccountContext.ConnectionStringName);
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
