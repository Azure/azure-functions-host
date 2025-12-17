using System.Collections.ObjectModel;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Extensions.Logging;
using OutOfProcModel.Abstractions.Worker;

namespace Microsoft.Azure.Functions.WorkerModel.Descriptor
{
    internal class WorkerModelFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly IWorkerResolver _workerResolver;

        public WorkerModelFunctionDescriptorProvider(ScriptHost host, ScriptJobHostOptions config, ICollection<IScriptBindingProvider> bindingProviders,
             ILoggerFactory loggerFactory, IWorkerResolver workerResolver)
             : base(host, config, bindingProviders)
        {
            _loggerFactory = loggerFactory;
            _workerResolver = workerResolver;
        }

        protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            return new WorkerModelFunctionInvoker(triggerMetadata, functionMetadata, _loggerFactory, inputBindings, outputBindings, _workerResolver);
        }
    }
}
