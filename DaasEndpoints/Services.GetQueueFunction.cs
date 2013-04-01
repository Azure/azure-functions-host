using System;
using System.IO;
using AzureTables;
using Executor;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.StorageClient;
using Orchestrator;
using RunnerInterfaces;
using SimpleBatch;

namespace DaasEndpoints
{
    public partial class Services
    {
        // Get the object that will queue function invoke requests to the execution substrate.
        // This may pick from multiple substrates.
        public IQueueFunction GetExecutionClient()
        {
            // Pick the appropriate queuing function to use.
            return GetWorkerRoleQueueFunction();
        }

        // Run via Azure tasks. 
        // This requires that an existing azure task pool has been setup. 
        private IQueueFunction GetAzureTasksQueueFunction()
        {
            IFunctionUpdatedLogger logger = GetFunctionInvokeLogger();
            ICausalityLogger causalityLogger = GetCausalityLogger();

            // Based on AzureTasks
            TaskConfig taskConfig = GetAzureTaskConfig();
            return new TaskExecutor(this._accountInfo, logger, taskConfig, causalityLogger);            
        }

        // Gets AzureTask configuration from the Azure config settings
        private static TaskConfig GetAzureTaskConfig()
        {
            var taskConfig = new TaskConfig
            {
                TenantUrl = RoleEnvironment.GetConfigurationSettingValue("AzureTaskTenantUrl"),
                AccountName = RoleEnvironment.GetConfigurationSettingValue("AzureTaskAccountName"),
                Key = RoleEnvironment.GetConfigurationSettingValue("AzureTaskKey"),
                PoolName = RoleEnvironment.GetConfigurationSettingValue("AzureTaskPoolName")
            };
            return taskConfig;
        }

        // Run via Antares. 
        // This requires that an existing antares site was deployed. 
        private IQueueFunction GetAntaresQueueFunction()
        {
            IFunctionUpdatedLogger logger = GetFunctionInvokeLogger();
            ICausalityLogger causalityLogger = GetCausalityLogger();

            // $$$ Get the URL from config? 
            string urlBase = "http://simplebatchworker.azurewebsites.net";
            var queue = this.GetExecutionQueue();
            return new AntaresRoleExecutionClient(urlBase, queue, this._accountInfo, logger, causalityLogger);
        }

        // Run via Azure Worker Roles
        // These worker roles should have been deployed automatically.
        private IQueueFunction GetWorkerRoleQueueFunction()
        {
            IFunctionUpdatedLogger logger = GetFunctionInvokeLogger();
            ICausalityLogger causalityLogger = GetCausalityLogger();

            // Based on WorkerRoles (submitted via a Queue)
            var queue = this.GetExecutionQueue();
            return new WorkerRoleExecutionClient(queue, this._accountInfo, logger, causalityLogger);            
        }
    }
}