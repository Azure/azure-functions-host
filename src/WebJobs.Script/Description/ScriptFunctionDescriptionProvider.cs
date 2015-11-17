// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class ScriptFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        private readonly string _applicationRoot;

        public ScriptFunctionDescriptorProvider(string applicationRoot)
        {
            _applicationRoot = applicationRoot;
        }

        public override bool TryCreate(FunctionInfo function, out FunctionDescriptor functionDescriptor)
        {
            functionDescriptor = null;

            string extension = Path.GetExtension(function.Source).ToLower();
            if (!ScriptFunctionInvoker.IsSupportedScriptType(extension))
            {
                return false;
            }

            string scriptFilePath = Path.Combine(_applicationRoot, "scripts", function.Source);
            ScriptFunctionInvoker invoker = new ScriptFunctionInvoker(scriptFilePath);

            JObject trigger = (JObject)function.Configuration["trigger"];
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

            functionDescriptor = new FunctionDescriptor
            {
                Name = function.Name,
                Invoker = invoker,
                Parameters = parameters
            };

            return true;
        }
    }
}
