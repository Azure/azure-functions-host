using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using DaasEndpoints;
using Executor;
using Microsoft.WindowsAzure;
using Orchestrator;
using RunnerInterfaces;
using WebFrontEnd.Controllers;

namespace WebFrontEnd
{
    internal static class ModelBinderConfig
    {
        public static void Register()
        {
            ModelBinders.Binders.Add(typeof(FunctionDefinition), new FunctionIndexEntityBinder());
            ModelBinders.Binders.Add(typeof(CloudStorageAccount), new CloudStorageAccountBinder());
            ModelBinders.Binders.Add(typeof(CloudBlobPath), new CloudBlobPathBinder());
            ModelBinders.Binders.Add(typeof(ExecutionInstanceLogEntity), new ExecutionInstanceLogEntityBinder());
            ModelBinders.Binders.Add(typeof(FunctionInvokeRequest), new FunctionInstanceBinder());
        }
    }

    // Bind FunctionIndexEntity
    public class FunctionInstanceBinder : ExecutionInstanceLogEntityBinder
    {
        public override object Parse(string value, string modelName, ModelStateDictionary modelState)
        {
            ExecutionInstanceLogEntity log = ParseWorker(value, modelName, modelState);
            if (log == null)
            {
                return null;
            }
            return log.FunctionInstance;            
        }
    }

    // Bind FunctionIndexEntity
    public class ExecutionInstanceLogEntityBinder : StringHelperModelBinder
    {
        public override object Parse(string value, string modelName, ModelStateDictionary modelState)
        {
            return ParseWorker(value, modelName, modelState);
        }

        protected ExecutionInstanceLogEntity ParseWorker(string value, string modelName, ModelStateDictionary modelState)
        {
            Guid g;
            if (!Guid.TryParse(value, out g))
            {
                modelState.AddModelError(modelName, "Invalid function log format");
                return null;
            }


            AzureRoleAccountInfo accountInfo = new AzureRoleAccountInfo();
            var services = new Services(accountInfo);
            IFunctionInstanceLookup logger = services.GetFunctionInstanceQuery();
            
            ExecutionInstanceLogEntity log = logger.Lookup(g);
            if (log == null)
            {
                modelState.AddModelError(modelName, "Invalid function log entry. Either the entry is invalid or logs have been deleted from the server.");
            }
            return log;
        }
    }

    // When derived class binds from a string
    public abstract class StringHelperModelBinder : IModelBinder
    {
        public object BindModel(ControllerContext controllerContext, ModelBindingContext bindingContext)
        {
            string name = bindingContext.ModelName;
            var r = bindingContext.ValueProvider.GetValue(name);
            if (r == null)
            {
                // This means client sent up missing data. 
                bindingContext.ModelState.AddModelError(name, "Missing " + name);
                return null;
            }
            string functionId = r.AttemptedValue as string;
            if (string.IsNullOrWhiteSpace(functionId))
            {
                return null;
            }
                        
            return Parse(functionId, name, bindingContext.ModelState);
        }

        public abstract object Parse(string value, string modelName, ModelStateDictionary modelState);
    }

    // Bind FunctionIndexEntity
    public class FunctionIndexEntityBinder : StringHelperModelBinder
    {
        public override object Parse(string value, string modelName, ModelStateDictionary modelState)
        {
            AzureRoleAccountInfo accountInfo = new AzureRoleAccountInfo();
            var services = new Services(accountInfo);

            FunctionDefinition func = services.GetFunctionTable().Lookup(value);
            if (func == null)
            {
                modelState.AddModelError(modelName, "Invalid function id");
            }            
            return func;
        }
    }

    // Bind FunctionIndexEntity
    public class CloudBlobPathBinder : StringHelperModelBinder
    {
        public override object Parse(string value, string modelName, ModelStateDictionary modelState)
        {
            CloudBlobPath path;
            if (CloudBlobPath.TryParse(value, out path))
            {
                return path;
            }

            modelState.AddModelError(modelName, "Illegal path syntax");
            return null;
        }
    }

    public class CloudStorageAccountBinder : StringHelperModelBinder
    {
        public override object Parse(string accountName, string modelName, ModelStateDictionary modelState)
        {
            string connection = HomeController.TryLookupConnectionString(accountName);
            if (connection == null)
            {
                modelState.AddModelError(modelName, "account key information is unavailable");
                return null;
            }

            return Utility.GetAccount(connection);
        }
    }
}