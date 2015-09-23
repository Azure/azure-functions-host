// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class CSharpFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        private readonly Type[] _types;

        public CSharpFunctionDescriptorProvider(Assembly sourceAssembly)
        {
            _types = sourceAssembly.GetTypes();
        }

        public override bool TryCreate(JObject function, out FunctionDescriptor functionDescriptor)
        {
            functionDescriptor = null;

            string name = (string)function["name"];
            if (string.IsNullOrEmpty(name))
            {
                // if a method name isn't explicitly provided, derive it
                // from the script file name
                string source = (string)function["source"];
                name = Path.GetFileNameWithoutExtension(source);
            }
            MethodInfo method = FindMethod(name);
            if (method == null)
            {
                throw new InvalidOperationException(string.Format("Unable to bind to method '{0}'", name));
            }

            MethodInvoker invoker = new MethodInvoker(method);

            JObject trigger = (JObject)function["trigger"];
            string triggerType = (string)trigger["type"];

            // TODO: match based on name? Or is positional convention OK?
            ParameterInfo[] sourceParameters = invoker.Target.GetParameters();
            ParameterInfo targetTriggerParameter = sourceParameters[0];
            Type triggerParameterType = targetTriggerParameter.ParameterType;

            string parameterName = (string)trigger["name"];
            if (string.IsNullOrEmpty(parameterName))
            {
                // default the name to the actual source parameter name
                trigger["name"] = targetTriggerParameter.Name;
            }

            ParameterDescriptor triggerParameter = null;
            switch (triggerType)
            {
                case "queue":
                    triggerParameter = ParseQueueTrigger(trigger, triggerParameterType);
                    break;
                case "blob":
                    triggerParameter = ParseBlobTrigger(trigger, triggerParameterType);
                    break;
                case "serviceBus":
                    triggerParameter = ParseServiceBusTrigger(trigger, triggerParameterType);
                    break;
                case "timer":
                    triggerParameter = ParseTimerTrigger(trigger, triggerParameterType);
                    break;
                case "webHook":
                    triggerParameter = ParseWebHookTrigger(trigger, typeof(HttpRequestMessage));
                    break;
            }

            Collection<ParameterDescriptor> parameters = new Collection<ParameterDescriptor>();
            parameters.Add(triggerParameter);

            // now add any additional parameters found on the source method
            // TODO: restrict to certain types only?
            foreach (ParameterInfo sourceParameter in sourceParameters.Skip(1))
            {
                ParameterDescriptor parameter = new ParameterDescriptor
                {
                    Name = sourceParameter.Name,
                    Type = sourceParameter.ParameterType
                };
                parameters.Add(parameter);
            }

            functionDescriptor = new FunctionDescriptor
            {
                Name = name,
                Invoker = invoker,
                Parameters = parameters
            };

            return true;
        }

        private MethodInfo FindMethod(string methodName)
        {
            foreach (Type type in _types)
            {
                foreach (MethodInfo currMethod in type.GetMethods())
                {
                    if (string.Compare(currMethod.Name, methodName, true) == 0)
                    {
                        return currMethod;
                    }
                }
            }
            return null;
        }
    }
}
