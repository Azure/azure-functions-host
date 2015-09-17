// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    public class CSharpFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        private readonly Type[] _types;

        public CSharpFunctionDescriptorProvider(Assembly sourceAssembly)
        {
            _types = sourceAssembly.GetTypes();
        }

        public override bool TryCreate(JObject function, out FunctionDescriptor functionDescriptor)
        {
            functionDescriptor = null;

            string scriptType = (string)function["type"];
            if (scriptType.ToLowerInvariant() != "csharp")
            {
                return false;
            }

            string name = (string)function["name"];

            MethodInfo method = null;
            foreach (Type type in _types)
            {
                foreach (MethodInfo currMethod in type.GetMethods())
                {
                    if (currMethod.Name == name)
                    {
                        method = currMethod;
                        break;
                    }
                }
            }
            if (method == null)
            {
                throw new InvalidOperationException(string.Format("Unable to bind to method '{0}'", name));
            }

            MethodInvoker invoker = new MethodInvoker(method);

            JObject trigger = (JObject)function["trigger"];
            string triggerType = (string)trigger["type"];

            // TODO: match based on name? Or is positional convention OK?
            ParameterInfo targetTriggerParameter = invoker.Target.GetParameters()[0];
            Type triggerParameterType = targetTriggerParameter.ParameterType;

            ParameterDescriptor triggerParameter = null;
            switch (triggerType)
            {
                case "queue":
                    triggerParameter = ParseQueueTrigger(trigger, triggerParameterType);
                    break;
                case "timer":
                    triggerParameter = ParseTimerTrigger(trigger, triggerParameterType);
                    break;
                case "webhook":
                    triggerParameter = ParseWebHookTrigger(trigger, typeof(HttpRequestMessage));
                    break;
            }

            Collection<ParameterDescriptor> parameters = new Collection<ParameterDescriptor>();
            parameters.Add(triggerParameter);

            functionDescriptor = new FunctionDescriptor
            {
                Name = name,
                ReturnType = typeof(void),
                Invoker = invoker,
                Parameters = parameters
            };

            return true;
        }

        public class MethodInvoker : IFunctionInvoker
        {
            private MethodInfo _method;

            public MethodInvoker(MethodInfo method)
            {
                _method = method;
            }

            public MethodInfo Target
            {
                get
                {
                    return _method;
                }
            }

            public object Invoke(object[] parameters)
            {
                return _method.Invoke(null, parameters);
            }
        }
    }
}
