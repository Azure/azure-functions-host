// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using EdgeJs;
using Microsoft.Azure.WebJobs.Extensions.Timers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Node
{
    public class NodeFunctionDescriptionProvider : FunctionDescriptionProvider
    {
        private readonly string _applicationRoot;

        public NodeFunctionDescriptionProvider(string applicationRoot)
        {
            _applicationRoot = applicationRoot;
        }

        public override bool TryCreate(JObject function, out FunctionDescriptor functionDescriptor)
        {
            functionDescriptor = null;

            string scriptType = (string)function["type"];
            if (scriptType.ToLowerInvariant() != "node")
            {
                return false;
            }

            string sourceFileName = (string)function["source"];
            string scriptFilePath = Path.Combine(_applicationRoot, "scripts", sourceFileName);
            string script = File.ReadAllText(scriptFilePath);
            ScriptInvoker invoker = new ScriptInvoker(script);

            JObject trigger = (JObject)function["trigger"];
            string triggerType = (string)trigger["type"];

            ParameterDescriptor triggerParameter = null;
            switch (triggerType)
            {
                case "queue":
                    triggerParameter = ParseQueueTrigger(trigger);
                    break;
                case "timer":
                    // TODO: Timer doesn't currently support string binding
                    triggerParameter = ParseTimerTrigger(trigger, typeof(TimerInfo));
                    break;
                case "webhook":
                    triggerParameter = ParseWebHookTrigger(trigger, typeof(string));
                    break;
            }

            Collection<ParameterDescriptor> parameters = new Collection<ParameterDescriptor>();
            parameters.Add(triggerParameter);

            string name = (string)function["name"];
            functionDescriptor = new FunctionDescriptor
            {
                Name = name,
                ReturnType = typeof(void),
                Invoker = invoker,
                Parameters = parameters
            };

            return true;
        }

        public class ScriptInvoker : IFunctionInvoker
        {
            private readonly Func<object, Task<object>> _scriptFunc;

            public ScriptInvoker(string script)
            {
                script = "return " + script;
                _scriptFunc = Edge.Func(script);
            }

            public object Invoke(object[] parameters)
            {
                // TODO: Decide how to handle this
                Type triggerParameterType = parameters[0].GetType();
                if (triggerParameterType == typeof(string))
                {
                    // convert string into Dictionary which Edge will convert into an object
                    // before invoking the function
                    parameters[0] = JsonConvert.DeserializeObject<Dictionary<string, object>>((string)parameters[0]);
                }

                Task<object> task = _scriptFunc(parameters[0]);
                task.Wait();

                return null;
            }
        }
    }
}
