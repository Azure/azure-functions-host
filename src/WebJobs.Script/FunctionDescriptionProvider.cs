// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Azure.WebJobs.Extensions.Timers;
using Microsoft.Azure.WebJobs.Extensions.WebHooks;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    public abstract class FunctionDescriptionProvider
    {
        public abstract bool TryCreate(JObject function, out FunctionDescriptor functionDescriptor);

        protected ParameterDescriptor ParseQueueTrigger(JObject trigger, Type triggerParameterType = null)
        {
            if (triggerParameterType == null)
            {
                triggerParameterType = typeof(string);
            }

            ConstructorInfo ctorInfo = typeof(QueueTriggerAttribute).GetConstructor(new Type[] { typeof(string) });
            string queueName = (string)trigger["queueName"];
            CustomAttributeBuilder attributeBuilder = new CustomAttributeBuilder(ctorInfo, new object[] { queueName });

            string parameterName = (string)trigger["name"];
            ParameterDescriptor triggerParameter = new ParameterDescriptor
            {
                Name = parameterName,
                Type = triggerParameterType,
                Attributes = ParameterAttributes.None,
                CustomAttributes = new Collection<CustomAttributeBuilder>
                {
                    attributeBuilder
                }
            };

            return triggerParameter;
        }

        protected ParameterDescriptor ParseTimerTrigger(JObject trigger, Type triggerParameterType = null)
        {
            if (triggerParameterType == null)
            {
                triggerParameterType = typeof(string);
            }

            ConstructorInfo ctorInfo = typeof(TimerTriggerAttribute).GetConstructor(new Type[] { typeof(string) });
            string schedule = (string)trigger["schedule"];
            CustomAttributeBuilder attributeBuilder = new CustomAttributeBuilder(ctorInfo, new object[] { schedule });

            string parameterName = (string)trigger["name"];
            ParameterDescriptor triggerParameter = new ParameterDescriptor
            {
                Name = parameterName,
                Type = triggerParameterType,
                Attributes = ParameterAttributes.None,
                CustomAttributes = new Collection<CustomAttributeBuilder>
                {
                    attributeBuilder
                }
            };

            return triggerParameter;
        }

        protected ParameterDescriptor ParseWebHookTrigger(JObject trigger, Type triggerParameterType = null)
        {
            if (triggerParameterType == null)
            {
                triggerParameterType = typeof(string);
            }

            ConstructorInfo ctorInfo = typeof(WebHookTriggerAttribute).GetConstructor(new Type[] { typeof(string) });
            string route = (string)trigger["route"];
            CustomAttributeBuilder attributeBuilder = new CustomAttributeBuilder(ctorInfo, new object[] { route });

            string parameterName = (string)trigger["name"];
            ParameterDescriptor triggerParameter = new ParameterDescriptor
            {
                Name = parameterName,
                Type = triggerParameterType,
                Attributes = ParameterAttributes.None,
                CustomAttributes = new Collection<CustomAttributeBuilder>
                {
                    attributeBuilder
                }
            };

            return triggerParameter;
        }
    }
}
