// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class NodeFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        private readonly string _rootPath;

        public NodeFunctionDescriptorProvider(string rootPath)
        {
            _rootPath = rootPath;
        }

        public override bool TryCreate(FunctionFolderInfo functionFolderInfo, out FunctionDescriptor functionDescriptor)
        {
            functionDescriptor = null;

            // name might point to a single file, or a module
            string extension = Path.GetExtension(functionFolderInfo.Source).ToLower();
            if (!(extension == ".js" || string.IsNullOrEmpty(extension)))
            {
                return false;
            }

            NodeFunctionInvoker invoker = new NodeFunctionInvoker(functionFolderInfo.Source);

            JObject trigger = (JObject)functionFolderInfo.Configuration["trigger"];
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
                case "queue":
                    triggerParameter = ParseQueueTrigger(trigger);
                    break;
                case "blob":
                    triggerParameter = ParseBlobTrigger(trigger);
                    break;
                case "serviceBus":
                    triggerParameter = ParseServiceBusTrigger(trigger);
                    break;
                case "timer":
                    triggerParameter = ParseTimerTrigger(trigger, typeof(TimerInfo));
                    break;
                case "webHook":
                    triggerParameter = ParseWebHookTrigger(trigger);
                    break;
            }

            Collection<ParameterDescriptor> parameters = new Collection<ParameterDescriptor>();
            parameters.Add(triggerParameter);

            // Add a TraceWriter for logging
            parameters.Add(new ParameterDescriptor("log", typeof(TraceWriter)));

            // Add an IBinder to support the binding programming model
            parameters.Add(new ParameterDescriptor("binder", typeof(IBinder)));

            functionDescriptor = new FunctionDescriptor(functionFolderInfo.Name, invoker, parameters);

            return true;
        }
    }
}
