// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public abstract class FunctionDescriptorProvider
    {
        public abstract bool TryCreate(FunctionMetadata metadata, out FunctionDescriptor functionDescriptor);

        protected ParameterDescriptor ParseQueueTrigger(QueueBindingMetadata trigger, Type triggerParameterType = null)
        {
            if (triggerParameterType == null)
            {
                triggerParameterType = typeof(string);
            }

            ConstructorInfo ctorInfo = typeof(QueueTriggerAttribute).GetConstructor(new Type[] { typeof(string) });
            string queueName = trigger.QueueName;
            CustomAttributeBuilder attributeBuilder = new CustomAttributeBuilder(ctorInfo, new object[] { queueName });

            string parameterName = trigger.Name;
            var attributes = new Collection<CustomAttributeBuilder>
            {
                attributeBuilder
            };
            return new ParameterDescriptor(parameterName, triggerParameterType, attributes);
        }

        protected ParameterDescriptor ParseBlobTrigger(BlobBindingMetadata trigger, Type triggerParameterType = null)
        {
            if (triggerParameterType == null)
            {
                triggerParameterType = typeof(string);
            }

            ConstructorInfo ctorInfo = typeof(BlobTriggerAttribute).GetConstructor(new Type[] { typeof(string) });
            string blobPath = trigger.Path;
            CustomAttributeBuilder attributeBuilder = new CustomAttributeBuilder(ctorInfo, new object[] { blobPath });

            string parameterName = trigger.Name;
            var attributes = new Collection<CustomAttributeBuilder>
            {
                attributeBuilder
            };
            return new ParameterDescriptor(parameterName, triggerParameterType, attributes);
        }

        protected ParameterDescriptor ParseServiceBusTrigger(ServiceBusBindingMetadata trigger, Type triggerParameterType = null)
        {
            if (triggerParameterType == null)
            {
                triggerParameterType = typeof(string);
            }

            string queueName = trigger.QueueName;
            string topicName = trigger.TopicName;
            string subscriptionName = trigger.SubscriptionName;
            AccessRights accessRights = trigger.AccessRights;

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

            string parameterName = trigger.Name;
            var attributes = new Collection<CustomAttributeBuilder>
            {
                attributeBuilder
            };
            return new ParameterDescriptor(parameterName, triggerParameterType, attributes);
        }

        protected ParameterDescriptor ParseTimerTrigger(TimerBindingMetadata trigger, Type triggerParameterType = null)
        {
            if (triggerParameterType == null)
            {
                triggerParameterType = typeof(string);
            }

            ConstructorInfo ctorInfo = typeof(TimerTriggerAttribute).GetConstructor(new Type[] { typeof(string) });
            string schedule = trigger.Schedule;
            bool runOnStartup = trigger.RunOnStartup;
            
            PropertyInfo runOnStartupProperty = typeof(TimerTriggerAttribute).GetProperty("RunOnStartup");
            CustomAttributeBuilder attributeBuilder = new CustomAttributeBuilder(ctorInfo, 
                new object[] { schedule }, 
                new PropertyInfo[] { runOnStartupProperty }, 
                new object[] { runOnStartup });

            string parameterName = trigger.Name;
            var attributes = new Collection<CustomAttributeBuilder>
            {
                attributeBuilder
            };
            return new ParameterDescriptor(parameterName, triggerParameterType, attributes);
        }

        protected ParameterDescriptor ParseHttpTrigger(HttpBindingMetadata trigger, Collection<CustomAttributeBuilder> methodAttributes, Type triggerParameterType = null)
        {
            if (triggerParameterType == null)
            {
                triggerParameterType = typeof(string);
            }

            ConstructorInfo ctorInfo = typeof(NoAutomaticTriggerAttribute).GetConstructor(new Type[0]);
            CustomAttributeBuilder attributeBuilder = new CustomAttributeBuilder(ctorInfo, new object[0]);
            methodAttributes.Add(attributeBuilder);

            ctorInfo = typeof(TraceLevelAttribute).GetConstructor(new Type[] { typeof(TraceLevel) });
            attributeBuilder = new CustomAttributeBuilder(ctorInfo, new object[] { TraceLevel.Off });
            methodAttributes.Add(attributeBuilder);

            string parameterName = trigger.Name;
            return new ParameterDescriptor(parameterName, triggerParameterType);
        }

        protected ParameterDescriptor ParseManualTrigger(BindingMetadata trigger, Collection<CustomAttributeBuilder> methodAttributes, Type triggerParameterType = null)
        {
            if (triggerParameterType == null)
            {
                triggerParameterType = typeof(string);
            }

            ConstructorInfo ctorInfo = typeof(NoAutomaticTriggerAttribute).GetConstructor(new Type[0]);
            CustomAttributeBuilder attributeBuilder = new CustomAttributeBuilder(ctorInfo, new object[0]);
            methodAttributes.Add(attributeBuilder);

            string parameterName = trigger.Name;
            return new ParameterDescriptor(parameterName, triggerParameterType);
        }
    }
}
