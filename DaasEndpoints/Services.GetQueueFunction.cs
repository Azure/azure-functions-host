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


        public IActivateFunction GetActivator(QueueInterfaces qi = null)
        {
            var q = GetQueueFunctionInternal(qi);
            return q;
        }

        // Get the object that will queue function invoke requests to the execution substrate.
        // This may pick from multiple substrates.
        public IQueueFunction GetQueueFunction(QueueInterfaces qi = null)
        {
            return GetQueueFunctionInternal(qi);
        }

        private QueueFunctionBase GetQueueFunctionInternal(QueueInterfaces qi = null)
        {        
            if (qi == null)
            {
                qi = this.GetQueueInterfaces();
            }
            // Pick the appropriate queuing function to use.
            QueueFunctionType t = GetExecutionType();
            // Keep a runtime codepath for all cases so that we ensure all cases always compile.
            switch (t)
            {
                case QueueFunctionType.Antares:
                    return GetAntaresQueueFunction(qi);
                case QueueFunctionType.AzureTasks:
                    return GetAzureTasksQueueFunction(qi);
                case QueueFunctionType.WorkerRoles:
                    return GetWorkerRoleQueueFunction(qi);
                case QueueFunctionType.Kudu:
                    return GetKuduQueueFunction(qi);
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

        // $$$ Returning bundles of interfaces... this is really looking like we need IOC.
        // Similar bundle with FunctionExecutionContext
        public QueueInterfaces GetQueueInterfaces()
        {
            var x = GetFunctionUpdatedLogger();

            return new QueueInterfaces
            {
                 AccountInfo = this._accountInfo,
                 Logger = x,
                 Lookup = x,
                 CausalityLogger = GetCausalityLogger(),
                 PreqreqManager = GetPrereqManager(x)
            };
        }

        // Run via Azure tasks. 
        // This requires that an existing azure task pool has been setup. 
        private QueueFunctionBase GetAzureTasksQueueFunction(QueueInterfaces qi)
        {
            // Based on AzureTasks
            TaskConfig taskConfig = GetAzureTaskConfig();
            return new TaskExecutor(taskConfig, qi);            
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
        private QueueFunctionBase GetAntaresQueueFunction(QueueInterfaces qi)
        {
            // Get url for notifying Antares worker. Eg, like: http://simplebatchworker.azurewebsites.net
            string urlBase = RoleEnvironment.GetConfigurationSettingValue("AntaresWorkerUrl");
           
            var queue = this.GetExecutionQueue();
            return new AntaresRoleExecutionClient(urlBase, queue, qi);
        }

        private QueueFunctionBase GetKuduQueueFunction(QueueInterfaces qi)
        {
            var queue = this.GetExecutionQueue();
            return new KuduQueueFunction(qi);  
        }

        // Run via Azure Worker Roles
        // These worker roles should have been deployed automatically.
        private QueueFunctionBase GetWorkerRoleQueueFunction(QueueInterfaces qi)
        {
            // Based on WorkerRoles (submitted via a Queue)
            var queue = this.GetExecutionQueue();
            return new WorkerRoleExecutionClient(queue, qi);
        }
    }
}