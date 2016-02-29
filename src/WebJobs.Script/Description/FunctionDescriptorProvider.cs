// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public abstract class FunctionDescriptorProvider
    {
        protected FunctionDescriptorProvider(ScriptHost host, ScriptHostConfiguration config)
        {
            Host = host;
            Config = config;
        }

        protected ScriptHost Host { get; private set; }

        protected ScriptHostConfiguration Config { get; private set; }

        public virtual bool TryCreate(FunctionMetadata functionMetadata, out FunctionDescriptor functionDescriptor)
        {
            if (functionMetadata == null)
            {
                throw new InvalidOperationException("functionMetadata");
            }

            functionDescriptor = null;

            if (functionMetadata.IsDisabled)
            {
                return false;
            }

            // parse the bindings
            Collection<FunctionBinding> inputBindings = FunctionBinding.GetBindings(Config, functionMetadata.InputBindings, FileAccess.Read);
            Collection<FunctionBinding> outputBindings = FunctionBinding.GetBindings(Config, functionMetadata.OutputBindings, FileAccess.Write);

            BindingMetadata triggerMetadata = functionMetadata.InputBindings.FirstOrDefault(p => p.IsTrigger);
            BindingType triggerType = triggerMetadata.Type;
            string triggerParameterName = triggerMetadata.Name;
            bool triggerNameSpecified = true;
            if (string.IsNullOrEmpty(triggerParameterName))
            {
                // default the name to simply 'input'
                triggerMetadata.Name = triggerParameterName = "input";
                triggerNameSpecified = false;
            }

            Collection<CustomAttributeBuilder> methodAttributes = new Collection<CustomAttributeBuilder>();
            ParameterDescriptor triggerParameter = null;
            bool omitInputParameter = false;
            switch (triggerType)
            {
                case BindingType.QueueTrigger:
                    triggerParameter = ParseQueueTrigger((QueueBindingMetadata)triggerMetadata);
                    break;
                case BindingType.EventHubTrigger:
                    triggerParameter = ParseEventHubTrigger((EventHubBindingMetadata)triggerMetadata);
                    break;
                case BindingType.BlobTrigger:
                    triggerParameter = ParseBlobTrigger((BlobBindingMetadata)triggerMetadata);
                    break;
                case BindingType.ServiceBusTrigger:
                    triggerParameter = ParseServiceBusTrigger((ServiceBusBindingMetadata)triggerMetadata);
                    break;
                case BindingType.TimerTrigger:
                    omitInputParameter = true;
                    triggerParameter = ParseTimerTrigger((TimerBindingMetadata)triggerMetadata, typeof(TimerInfo));
                    break;
                case BindingType.HttpTrigger:
                    if (!triggerNameSpecified)
                    {
                        triggerMetadata.Name = triggerParameterName = "req";
                    }
                    triggerParameter = ParseHttpTrigger((HttpBindingMetadata)triggerMetadata, methodAttributes, typeof(HttpRequestMessage));
                    break;
                case BindingType.ManualTrigger:
                    triggerParameter = ParseManualTrigger(triggerMetadata, methodAttributes);
                    break;
            }

            Collection<ParameterDescriptor> parameters = new Collection<ParameterDescriptor>();
            triggerParameter.IsTrigger = true;
            parameters.Add(triggerParameter);

            // Add a TraceWriter for logging
            parameters.Add(new ParameterDescriptor("log", typeof(TraceWriter)));

            // Add an IBinder to support the binding programming model
            parameters.Add(new ParameterDescriptor("binder", typeof(IBinder)));

            // Add ExecutionContext to provide access to InvocationId, etc.
            parameters.Add(new ParameterDescriptor("context", typeof(ExecutionContext)));

            string scriptFilePath = Path.Combine(Config.RootScriptPath, functionMetadata.Source);
            IFunctionInvoker invoker = CreateFunctionInvoker(scriptFilePath, triggerMetadata, functionMetadata, omitInputParameter, inputBindings, outputBindings);
            functionDescriptor = new FunctionDescriptor(functionMetadata.Name, invoker, functionMetadata, parameters, methodAttributes);

            return true;
        }

        protected abstract IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, bool omitInputParameter, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings);

        protected ParameterDescriptor ParseEventHubTrigger(EventHubBindingMetadata trigger, Type triggerParameterType = null)
        {
            if (trigger == null)
            {
                throw new ArgumentNullException("trigger");
            }
            if (triggerParameterType == null)
            {
                triggerParameterType = typeof(string);
            }

            ConstructorInfo ctorInfo = typeof(ServiceBus.EventHubTriggerAttribute).GetConstructor(new Type[] { typeof(string) });
            string queueName = trigger.Path;
            CustomAttributeBuilder attributeBuilder = new CustomAttributeBuilder(ctorInfo, new object[] { queueName });

            string parameterName = trigger.Name;
            var attributes = new Collection<CustomAttributeBuilder>
            {
                attributeBuilder
            };
            return new ParameterDescriptor(parameterName, triggerParameterType, attributes);
        }

        protected ParameterDescriptor ParseQueueTrigger(QueueBindingMetadata trigger, Type triggerParameterType = null)
        {
            if (trigger == null)
            {
                throw new ArgumentNullException("trigger");
            }

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
            if (trigger == null)
            {
                throw new ArgumentNullException("trigger");
            }

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
            if (trigger == null)
            {
                throw new ArgumentNullException("trigger");
            }

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
            if (trigger == null)
            {
                throw new ArgumentNullException("trigger");
            }

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
            if (trigger == null)
            {
                throw new ArgumentNullException("trigger");
            }

            if (methodAttributes == null)
            {
                throw new ArgumentNullException("methodAttributes");
            }

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
            if (trigger == null)
            {
                throw new ArgumentNullException("trigger");
            }

            if (methodAttributes == null)
            {
                throw new ArgumentNullException("methodAttributes");
            }

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
