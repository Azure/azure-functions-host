// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class ScriptFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        private readonly JobHostConfiguration _config;
        private readonly string _rootPath;

        public ScriptFunctionDescriptorProvider(JobHostConfiguration config, string rootPath)
        {
            _config = config;
            _rootPath = rootPath;
        }

        public override bool TryCreate(FunctionFolderInfo functionFolderInfo, out FunctionDescriptor functionDescriptor)
        {
            functionDescriptor = null;

            string extension = Path.GetExtension(functionFolderInfo.Source).ToLower();
            if (!ScriptFunctionInvoker.IsSupportedScriptType(extension))
            {
                return false;
            }

            // parse the bindings
            JObject bindings = (JObject)functionFolderInfo.Configuration["bindings"];
            JArray inputs = (JArray)bindings["input"];
            Collection<Binding> inputBindings = Binding.GetBindings(_config, inputs, FileAccess.Read);

            JArray outputs = (JArray)bindings["output"];
            Collection<Binding> outputBindings = Binding.GetBindings(_config, outputs, FileAccess.Write);

            string scriptFilePath = Path.Combine(_rootPath, functionFolderInfo.Source);
            ScriptFunctionInvoker invoker = new ScriptFunctionInvoker(scriptFilePath, inputBindings, outputBindings);

            JObject trigger = (JObject)inputs.FirstOrDefault(p => ((string)p["type"]).ToLowerInvariant().EndsWith("trigger"));
            string triggerType = (string)trigger["type"];

            string parameterName = (string)trigger["name"];
            if (string.IsNullOrEmpty(parameterName))
            {
                // default the name to simply 'input'
                trigger["name"] = "input";
            }

            ParameterDescriptor triggerParameter = null;
            switch (triggerType)
            {
                case "queueTrigger":
                    triggerParameter = ParseQueueTrigger(trigger);
                    break;
                case "blobTrigger":
                    triggerParameter = ParseBlobTrigger(trigger);
                    break;
                case "serviceBusTrigger":
                    triggerParameter = ParseServiceBusTrigger(trigger);
                    break;
                case "timerTrigger":
                    triggerParameter = ParseTimerTrigger(trigger, typeof(TimerInfo));
                    break;
                case "webHookTrigger":
                    triggerParameter = ParseWebHookTrigger(trigger);
                    break;
            }

            Collection<ParameterDescriptor> parameters = new Collection<ParameterDescriptor>();
            parameters.Add(triggerParameter);

            // Add a TraceWriter for logging
            parameters.Add(new ParameterDescriptor("log", typeof(TraceWriter)));

            // Add an IBinder to support output bindings
            parameters.Add(new ParameterDescriptor("binder", typeof(IBinder)));

            functionDescriptor = new FunctionDescriptor(functionFolderInfo.Name, invoker, parameters);

            return true;
        }
    }
}
