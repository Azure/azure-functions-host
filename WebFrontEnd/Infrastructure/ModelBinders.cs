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
using Ninject;

namespace WebFrontEnd
{
    internal static class ModelBinderConfig
    {
        public static void Register(IKernel kernel)
        {
            try
            {
                IFunctionInstanceLookup lookup = kernel.Get<IFunctionInstanceLookup>();
                IFunctionTableLookup functionTable = kernel.Get<IFunctionTableLookup>();

                ModelBinders.Binders.Add(typeof(FunctionDefinition), new FunctionIndexEntityBinder(functionTable));
                ModelBinders.Binders.Add(typeof(CloudBlobPath), new CloudBlobPathBinder());
                ModelBinders.Binders.Add(typeof(ExecutionInstanceLogEntity), new ExecutionInstanceLogEntityBinder(lookup));
                ModelBinders.Binders.Add(typeof(FunctionInvokeRequest), new FunctionInstanceBinder(lookup));

            }
            catch (ActivationException)
            {
                // Ignore. Storage account may be invalid. 
            }
        }
    }

    // Bind FunctionIndexEntity
    public class FunctionInstanceBinder : ExecutionInstanceLogEntityBinder
    {
        public FunctionInstanceBinder(IFunctionInstanceLookup logger)
            : base(logger)
        {
        }

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
        private readonly IFunctionInstanceLookup _logger;

        public ExecutionInstanceLogEntityBinder(IFunctionInstanceLookup logger)
        {
            _logger = logger;
        }

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
            
            ExecutionInstanceLogEntity log = _logger.Lookup(g);
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
        private readonly IFunctionTableLookup _functionTable;

        public FunctionIndexEntityBinder(IFunctionTableLookup functionTable)
        {
            _functionTable = functionTable;
        }

        public override object Parse(string value, string modelName, ModelStateDictionary modelState)
        {
            FunctionDefinition func = _functionTable.Lookup(value);
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
}