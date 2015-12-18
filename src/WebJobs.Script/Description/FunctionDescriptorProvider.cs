// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    public abstract class FunctionDescriptorProvider
    {
        public abstract bool TryCreate(FunctionFolderInfo functionFolderInfo, out FunctionDescriptor functionDescriptor);

        protected bool IsDisabled(string functionName, JObject trigger)
        {
            bool isDisabled = false;
            JToken isDisabledValue = trigger["disabled"];
            if (isDisabledValue != null)
            {
                if (isDisabledValue.Type == JTokenType.Boolean && (bool)isDisabledValue)
                {
                    isDisabled = true;
                }
                else
                {
                    string settingName = (string)isDisabledValue;
                    string value = Environment.GetEnvironmentVariable(settingName);
                    if (!string.IsNullOrEmpty(value) &&
                        (string.Compare(value, "1", StringComparison.OrdinalIgnoreCase) == 0 ||
                         string.Compare(value, "true", StringComparison.OrdinalIgnoreCase) == 0))
                    {
                        isDisabled = true;
                    }
                }
            }

            if (isDisabled)
            {
                Console.WriteLine(string.Format("Function '{0}' is disabled", functionName));
            }

            return isDisabled;
        }

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
            var attributes = new Collection<CustomAttributeBuilder>
            {
                attributeBuilder
            };
            return new ParameterDescriptor(parameterName, triggerParameterType, attributes);
        }

        protected ParameterDescriptor ParseBlobTrigger(JObject trigger, Type triggerParameterType = null)
        {
            if (triggerParameterType == null)
            {
                triggerParameterType = typeof(string);
            }

            ConstructorInfo ctorInfo = typeof(BlobTriggerAttribute).GetConstructor(new Type[] { typeof(string) });
            string blobPath = (string)trigger["path"];
            CustomAttributeBuilder attributeBuilder = new CustomAttributeBuilder(ctorInfo, new object[] { blobPath });

            string parameterName = (string)trigger["name"];
            var attributes = new Collection<CustomAttributeBuilder>
            {
                attributeBuilder
            };
            return new ParameterDescriptor(parameterName, triggerParameterType, attributes);
        }

        protected ParameterDescriptor ParseServiceBusTrigger(JObject trigger, Type triggerParameterType = null)
        {
            if (triggerParameterType == null)
            {
                triggerParameterType = typeof(string);
            }

            string queueName = (string)trigger["queueName"];
            string topicName = (string)trigger["topicName"];
            string subscriptionName = (string)trigger["subscriptionName"];

            string accessRightsValue = (string)trigger["accessRights"];
            AccessRights accessRights = AccessRights.Manage;
            if (!string.IsNullOrEmpty(accessRightsValue))
            {
                AccessRights parsed;
                if (Enum.TryParse<AccessRights>(accessRightsValue, true, out parsed))
                {
                    accessRights = parsed;
                }
            }

            CustomAttributeBuilder attributeBuilder = null;
            if (!string.IsNullOrEmpty(topicName) && !string.IsNullOrEmpty(subscriptionName))
            {
                ConstructorInfo ctorInfo = typeof(ServiceBusTriggerAttribute).GetConstructor(new Type[] { typeof(string), typeof(string), typeof(AccessRights) });
                attributeBuilder = new CustomAttributeBuilder(ctorInfo, new object[] { topicName, subscriptionName, accessRights });
            }
            else if (!string.IsNullOrEmpty(queueName))
            {
                ConstructorInfo ctorInfo = typeof(ServiceBusTriggerAttribute).GetConstructor(new Type[] { typeof(string), typeof(AccessRights) });
                attributeBuilder = new CustomAttributeBuilder(ctorInfo, new object[] { queueName, accessRights });
            }
            else
            {
                throw new InvalidOperationException("Invalid ServiceBus trigger configuration.");
            }

            string parameterName = (string)trigger["name"];
            var attributes = new Collection<CustomAttributeBuilder>
            {
                attributeBuilder
            };
            return new ParameterDescriptor(parameterName, triggerParameterType, attributes);
        }

        protected ParameterDescriptor ParseTimerTrigger(JObject trigger, Type triggerParameterType = null)
        {
            if (triggerParameterType == null)
            {
                triggerParameterType = typeof(string);
            }

            ConstructorInfo ctorInfo = typeof(TimerTriggerAttribute).GetConstructor(new Type[] { typeof(string) });
            string schedule = (string)trigger["schedule"];
            bool runOnStartup = false;
            JToken token = null;
            if (trigger.TryGetValue("runOnStartup", out token))
            {
                runOnStartup = token.Value<bool>();
            }
            PropertyInfo runOnStartupProperty = typeof(TimerTriggerAttribute).GetProperty("RunOnStartup");
            CustomAttributeBuilder attributeBuilder = new CustomAttributeBuilder(ctorInfo, 
                new object[] { schedule }, 
                new PropertyInfo[] { runOnStartupProperty }, 
                new object[] { runOnStartup });

            string parameterName = (string)trigger["name"];
            var attributes = new Collection<CustomAttributeBuilder>
            {
                attributeBuilder
            };
            return new ParameterDescriptor(parameterName, triggerParameterType, attributes);
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
            var attributes = new Collection<CustomAttributeBuilder>
            {
                attributeBuilder
            };
            return new ParameterDescriptor(parameterName, triggerParameterType, attributes);
        }
    }
}
