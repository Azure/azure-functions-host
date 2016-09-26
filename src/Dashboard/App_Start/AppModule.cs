// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Configuration;
using System.Web;
using System.Web.Caching;
using System.Web.Mvc;
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
using Ninject.Modules;
using Ninject.Web.Mvc.FilterBindingSyntax;

namespace Dashboard
{
    public class AppModule : NinjectModule
    {
        // Optional Appsetting to explicitly set the table name to use with fast logging. 
        private const string FunctionLogTableAppSettingName = "AzureWebJobsLogTableName";

        // Optional Appsetting to control site extension versioning. We can infer log mode from this. 
        private const string FunctionExtensionVersionAppSettingName = "FUNCTIONS_EXTENSION_VERSION";
        private const string FunctionExtensionVersionDisabled = "disabled";

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

            CloudTableClient tableClient = account.CreateCloudTableClient();
            CloudBlobClient blobClient = account.CreateCloudBlobClient();
            Bind<CloudStorageAccount>().ToConstant(account);
            Bind<CloudBlobClient>().ToConstant(blobClient);

            CloudTable logTable = TryGetLogTable(tableClient);

            if (logTable != null)
            {
                context.DisableInvoke = true;

                // fast table reader.                 
                var reader = LogFactory.NewReader(logTable);
                Bind<ILogReader>().ToConstant(reader);

                var s = new FastTableReader(reader);

                Bind<IFunctionLookup>().ToConstant(s);
                Bind<IFunctionInstanceLookup>().ToConstant(s);
                Bind<IFunctionStatisticsReader>().ToConstant(s);
                Bind<IFunctionIndexReader>().ToConstant(s);
                Bind<IRecentInvocationIndexByFunctionReader>().ToConstant(s);
                Bind<IRecentInvocationIndexReader>().ToConstant(s);

                Bind<IHostVersionReader>().To<NullHostVersionReader>();

                // See services used by FunctionsController
                Bind<IRecentInvocationIndexByJobRunReader>().To<NullInvocationIndexReader>();
                Bind<IRecentInvocationIndexByParentReader>().To<NullInvocationIndexReader>();

                Bind<IHeartbeatValidityMonitor>().To<NullHeartbeatValidityMonitor>();

                Bind<IAborter>().To<NullAborter>();
                Bind<IInvoker>().To<NullInvoker>();

                // for diagnostics
                Bind<IIndexerLogReader>().To<NullIIndexerLogReader>();
                Bind<IPersistentQueueReader<PersistentQueueMessage>>().To<NullLogReader>();
                Bind<IDashboardVersionManager>().To<NullIDashboardVersionManager>();
            }
            else
            {
                Bind<ILogReader>().ToConstant(new NullFastReader());

                // Traditional SDK reader. 

                CloudQueueClient queueClient = account.CreateCloudQueueClient();

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
        }

        // Get a fast log table name 
        // OR return null to use traditional logging. 
        private static CloudTable TryGetLogTable(CloudTableClient tableClient)
        {
            string logTableName = ConfigurationManager.AppSettings[FunctionLogTableAppSettingName];
            if (string.IsNullOrWhiteSpace(logTableName))
            {
                // Check for default name
                string defaultName = LogFactory.DefaultLogTableName;
                var table = tableClient.GetTableReference(defaultName);

                var ver = ConfigurationManager.AppSettings[FunctionExtensionVersionAppSettingName];
                if (!string.IsNullOrWhiteSpace(ver))
                {
                    if (string.Equals(ver, FunctionExtensionVersionDisabled, StringComparison.OrdinalIgnoreCase))
                    {
                        // Explicitly set to old mode. 
                        return null;
                    }
                    else
                    {
                        // Appsetting specifically opts us in. Use fast-tables.
                        table.CreateIfNotExists();
                        return table;
                    }
                }

                if (table.Exists())
                {
                    return table;
                }
            }
            else
            {
                // Name is explicitly supplied in an appsetting. Definitely using the fast tables. 
                var logTable = tableClient.GetTableReference(logTableName);
                return logTable;
            }

            return null;
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
