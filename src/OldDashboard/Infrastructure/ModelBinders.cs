using System;
using System.Web.Mvc;
using Ninject;
using Dashboard.Models.Protocol;
using Microsoft.WindowsAzure.Jobs;

namespace Dashboard
{
    internal static class ModelBinderConfig
    {
        public static void Register(IKernel kernel)
        {
            IFunctionInstanceLookup lookup = kernel.Get<IFunctionInstanceLookup>();
            IFunctionTableLookup functionTable = kernel.Get<IFunctionTableLookup>();

            ModelBinders.Binders.Add(typeof(FunctionDefinitionModel), new FunctionDefinitionModelBinder(functionTable));
            ModelBinders.Binders.Add(typeof(CloudBlobPathModel), new CloudBlobPathBinder());
            ModelBinders.Binders.Add(typeof(ExecutionInstanceLogEntityModel), new ExecutionInstanceLogEntityBinder(lookup));
            ModelBinders.Binders.Add(typeof(FunctionInvokeRequestModel), new FunctionInstanceBinder(lookup));
        }
    }

    // Bind FunctionIndexEntity
    internal class FunctionInstanceBinder : ExecutionInstanceLogEntityBinder
    {
        public FunctionInstanceBinder(IFunctionInstanceLookup logger)
            : base(logger)
        {
        }

        public override object Parse(string value, string modelName, ModelStateDictionary modelState)
        {
            var log = ParseWorker(value, modelName, modelState);
            if (log == null)
            {
                return null;
            }

            FunctionInvokeRequest request = log.FunctionInstance.UnderlyingObject;
            return new FunctionInvokeRequestModel(request);
        }
    }

    // Bind FunctionIndexEntity
    internal class ExecutionInstanceLogEntityBinder : StringHelperModelBinder
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

        protected ExecutionInstanceLogEntityModel ParseWorker(string value, string modelName, ModelStateDictionary modelState)
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

            return new ExecutionInstanceLogEntityModel(log);
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
    internal class FunctionDefinitionModelBinder : StringHelperModelBinder
    {
        private readonly IFunctionTableLookup _functionTable;

        public FunctionDefinitionModelBinder(IFunctionTableLookup functionTable)
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
            return new FunctionDefinitionModel(func);
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
                return new CloudBlobPathModel(path);
            }

            modelState.AddModelError(modelName, "Illegal path syntax");
            return null;
        }
    } 
}
