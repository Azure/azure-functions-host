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
        // Get a description of which execution mechanism is used. 
        // This is coupled to IQueueFunction. ($$$ Move this to be on that interface?)
        public string GetExecutionSubstrateDescription()
        {
            try
            {
                QueueFunctionType t = GetExecutionType();
                switch (t)
                {
                    case QueueFunctionType.Antares:
                        string url = RoleEnvironment.GetConfigurationSettingValue("AntaresWorkerUrl");
                        return "Antares: " + url;
                    default:
                        return t.ToString();
                }
            }
            catch (Exception e)
            {
                return e.Message;
            }

        }

        private QueueFunctionType GetExecutionType()
        {
            string value = RoleEnvironment.GetConfigurationSettingValue("ExecutionType");

            QueueFunctionType t;
            if (Enum.TryParse<QueueFunctionType>(value, out t))
            {
                return t;
            }
            string msg = string.Format("unknown execution substrate:{0}", value);
            throw new InvalidOperationException(msg);
        }

        // Get the object that will queue function invoke requests to the execution substrate.
        // This may pick from multiple substrates.
        public IQueueFunction GetQueueFunction()
        {
            // Pick the appropriate queuing function to use.
            QueueFunctionType t = GetExecutionType();
            // Keep a runtime codepath for all cases so that we ensure all cases always compile.
            switch (t)
            {
                case QueueFunctionType.Antares:
                    return GetAntaresQueueFunction();
                case QueueFunctionType.AzureTasks:
                    return GetAzureTasksQueueFunction();
                case QueueFunctionType.WorkerRoles:
                    return GetWorkerRoleQueueFunction();
                case QueueFunctionType.Kudu:
                    return GetKuduQueueFunction();
                default:
                    // should have already thrown before getting here. 
                    throw new InvalidOperationException("Unknown"); 
            }            
        }

        enum QueueFunctionType
        {
            WorkerRoles,
            Antares,
            AzureTasks,
            Kudu,
        }

        // Run via Azure tasks. 
        // This requires that an existing azure task pool has been setup. 
        private IQueueFunction GetAzureTasksQueueFunction()
        {
            IFunctionUpdatedLogger logger = GetFunctionUpdatedLogger();
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
            IFunctionUpdatedLogger logger = GetFunctionUpdatedLogger();
            ICausalityLogger causalityLogger = GetCausalityLogger();

            // Get url for notifying Antares worker. Eg, like: http://simplebatchworker.azurewebsites.net
            string urlBase = RoleEnvironment.GetConfigurationSettingValue("AntaresWorkerUrl");
           
            var queue = this.GetExecutionQueue();
            return new AntaresRoleExecutionClient(urlBase, queue, this._accountInfo, logger, causalityLogger);
        }

        private IQueueFunction GetKuduQueueFunction()
        {
            IFunctionUpdatedLogger logger = GetFunctionUpdatedLogger();
            ICausalityLogger causalityLogger = GetCausalityLogger();
                        
            var queue = this.GetExecutionQueue();
            return new KuduQueueFunction(this._accountInfo, logger, causalityLogger);  
        }

        // Run via Azure Worker Roles
        // These worker roles should have been deployed automatically.
        private IQueueFunction GetWorkerRoleQueueFunction()
        {
            IFunctionUpdatedLogger logger = GetFunctionUpdatedLogger();
            ICausalityLogger causalityLogger = GetCausalityLogger();

            // Based on WorkerRoles (submitted via a Queue)
            var queue = this.GetExecutionQueue();
            return new WorkerRoleExecutionClient(queue, this._accountInfo, logger, causalityLogger);            
        }
    }
}