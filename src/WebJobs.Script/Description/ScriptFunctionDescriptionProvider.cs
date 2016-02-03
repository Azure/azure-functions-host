// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection.Emit;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class ScriptFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        private readonly ScriptHostConfiguration _config;
        private readonly string _rootPath;

        public ScriptFunctionDescriptorProvider(ScriptHostConfiguration config)
        {
            _config = config;
            _rootPath = config.RootScriptPath;
        }

        public override bool TryCreate(FunctionMetadata metadata, out FunctionDescriptor functionDescriptor)
        {
            functionDescriptor = null;

            string extension = Path.GetExtension(metadata.Source).ToLower();
            if (!ScriptFunctionInvoker.IsSupportedScriptType(extension))
            {
                return false;
            }

            if (metadata.IsDisabled)
            {
                return false;
            }

            // parse the bindings
            Collection<FunctionBinding> inputBindings = FunctionBinding.GetBindings(_config, metadata.InputBindings, FileAccess.Read);
            Collection<FunctionBinding> outputBindings = FunctionBinding.GetBindings(_config, metadata.OutputBindings, FileAccess.Write);

            BindingMetadata triggerMetadata = metadata.InputBindings.FirstOrDefault(p => p.IsTrigger);
            BindingType triggerType = triggerMetadata.Type;
            string triggerParameterName = triggerMetadata.Name;
            bool triggerNameSpecified = true;
            if (string.IsNullOrEmpty(triggerParameterName))
            {
                // default the name to simply 'input'
                triggerMetadata.Name = "input";
                triggerNameSpecified = false;
            }

            Collection<CustomAttributeBuilder> methodAttributes = new Collection<CustomAttributeBuilder>();
            ParameterDescriptor triggerParameter = null;
            bool omitInputParameter = false;
            switch (triggerType)
            {
                case BindingType.QueueTrigger:
                    triggerParameter = ParseQueueTrigger((QueueBindingMetadata)triggerMetadata);
                    break;
                case BindingType.BlobTrigger:
                    triggerParameter = ParseBlobTrigger((BlobBindingMetadata)triggerMetadata);
                    break;
                case BindingType.ServiceBusTrigger:
                    triggerParameter = ParseServiceBusTrigger((ServiceBusBindingMetadata)triggerMetadata);
                    break;
                case BindingType.TimerTrigger:
                    omitInputParameter = true;
                    triggerParameter = ParseTimerTrigger((TimerBindingMetadata)triggerMetadata, typeof(TimerInfo));
                    break;
                case BindingType.HttpTrigger:
                    if (!triggerNameSpecified)
                    {
                        triggerMetadata.Name = triggerParameterName = "req";
                    }
                    triggerParameter = ParseHttpTrigger((HttpBindingMetadata)triggerMetadata, methodAttributes, typeof(HttpRequestMessage));
                    break;
                case BindingType.ManualTrigger:
                    triggerParameter = ParseManualTrigger(triggerMetadata, methodAttributes);
                    break;
            }

            Collection<ParameterDescriptor> parameters = new Collection<ParameterDescriptor>();
            triggerParameter.IsTrigger = true;
            parameters.Add(triggerParameter);

            // Add a TraceWriter for logging
            parameters.Add(new ParameterDescriptor("log", typeof(TraceWriter)));

            // Add an IBinder to support output bindings
            parameters.Add(new ParameterDescriptor("binder", typeof(IBinder)));

            // Add ExecutionContext to provide access to InvocationId, etc.
            parameters.Add(new ParameterDescriptor("context", typeof(ExecutionContext)));

            string scriptFilePath = Path.Combine(_rootPath, metadata.Source);
            ScriptFunctionInvoker invoker = new ScriptFunctionInvoker(scriptFilePath, _config, triggerMetadata, metadata, omitInputParameter, inputBindings, outputBindings);
            functionDescriptor = new FunctionDescriptor(metadata.Name, invoker, metadata, parameters, methodAttributes);

            return true;
        }
    }
}
