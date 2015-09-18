// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Azure.WebJobs.Extensions.Timers;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Node
{
    public class NodeFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        private readonly string _applicationRoot;

        public NodeFunctionDescriptorProvider(string applicationRoot)
        {
            _applicationRoot = applicationRoot;
        }

        public override bool TryCreate(JObject function, out FunctionDescriptor functionDescriptor)
        {
            functionDescriptor = null;

            string sourceFileName = (string)function["source"];
            string scriptFilePath = Path.Combine(_applicationRoot, "scripts", sourceFileName);
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
                case "timer":
                    triggerParameter = ParseTimerTrigger(trigger, typeof(TimerInfo));
                    break;
                case "webhook":
                    triggerParameter = ParseWebHookTrigger(trigger, typeof(string));
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

            string name = (string)function["name"];
            if (string.IsNullOrEmpty(name))
            {
                // if a method name isn't explicitly provided, derive it
                // from the script file name
                name = Path.GetFileNameWithoutExtension(sourceFileName);
                name = name.Substring(0, 1).ToUpper() + name.Substring(1);
            }

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
