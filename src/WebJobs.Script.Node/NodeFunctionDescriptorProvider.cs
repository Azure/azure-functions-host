// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Azure.WebJobs.Extensions.Timers;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Node
{
    internal class NodeFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        private readonly string _applicationRoot;

        public NodeFunctionDescriptorProvider(string applicationRoot)
        {
            _applicationRoot = applicationRoot;
        }

        public override bool TryCreate(JObject function, out FunctionDescriptor functionDescriptor)
        {
            functionDescriptor = null;

            // name might point to a single file, or a module
            string source = (string)function["source"];
            string name = (string)function["name"];
            if (string.IsNullOrEmpty(name))
            {
                // if a method name isn't explicitly provided, derive it
                // from the script file name
                name = Path.GetFileNameWithoutExtension(source);
                name = name.Substring(0, 1).ToUpper() + name.Substring(1);
            }

            string scriptFilePath = Path.Combine(_applicationRoot, "scripts", source);
            ScriptInvoker invoker = new ScriptInvoker(scriptFilePath);

            JObject trigger = (JObject)function["trigger"];
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
            ParameterDescriptor textWriter = new ParameterDescriptor
            {
                Name = "log",
                Type = typeof(TextWriter)
            };
            parameters.Add(textWriter);

            // Add an IBinder to support the binding programming model
            ParameterDescriptor binder = new ParameterDescriptor
            {
                Name = "binder",
                Type = typeof(IBinder)
            };
            parameters.Add(binder);

            functionDescriptor = new FunctionDescriptor
            {
                Name = name,
                Invoker = invoker,
                Parameters = parameters
            };

            return true;
        }
    }
}
