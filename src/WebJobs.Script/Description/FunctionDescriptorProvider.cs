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
using Microsoft.Azure.WebJobs.Extensions.ApiHub;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public abstract class FunctionDescriptorProvider
    {
        internal const string DefaultInputParameterName = "input";
        internal const string DefaultHttpInputParameterName = "req";

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

            // Default the trigger binding name if a name hasn't
            // been specified
            // TODO: Remove this logic and always require it to be explicitly
            // specified?
            foreach (var binding in functionMetadata.Bindings.Where(p => p.IsTrigger))
            {
                if (string.IsNullOrEmpty(binding.Name))
                {
                    if (binding.Type == BindingType.HttpTrigger)
                    {
                        binding.Name = DefaultHttpInputParameterName;
                    }
                    else
                    {
                        binding.Name = DefaultInputParameterName;
                    }
                }
            }

            // parse the bindings
            Collection<FunctionBinding> inputBindings = FunctionBinding.GetBindings(Config, functionMetadata.InputBindings, FileAccess.Read);
            Collection<FunctionBinding> outputBindings = FunctionBinding.GetBindings(Config, functionMetadata.OutputBindings, FileAccess.Write);

            BindingMetadata triggerMetadata = functionMetadata.InputBindings.FirstOrDefault(p => p.IsTrigger);
          
            string scriptFilePath = Path.Combine(Config.RootScriptPath, functionMetadata.Source);

            IFunctionInvoker invoker = null;

            try
            {
                invoker = CreateFunctionInvoker(scriptFilePath, triggerMetadata, functionMetadata, inputBindings, outputBindings);

                Collection<CustomAttributeBuilder> methodAttributes = new Collection<CustomAttributeBuilder>();
                Collection<ParameterDescriptor> parameters = GetFunctionParameters(invoker, functionMetadata, triggerMetadata, methodAttributes, inputBindings, outputBindings);

                functionDescriptor = new FunctionDescriptor(functionMetadata.Name, invoker, functionMetadata, parameters, methodAttributes);

                return true;
            }
            catch (Exception)
            {
                IDisposable disposableInvoker = invoker as IDisposable;
                if (disposableInvoker != null)
                {
                    disposableInvoker.Dispose();
                }

                throw;
            }
        }

        protected virtual Collection<ParameterDescriptor> GetFunctionParameters(IFunctionInvoker functionInvoker, FunctionMetadata functionMetadata, 
            BindingMetadata triggerMetadata, Collection<CustomAttributeBuilder> methodAttributes, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            if (functionInvoker == null)
            {
                throw new ArgumentNullException("functionInvoker");
            }
            if (functionMetadata == null)
            {
                throw new ArgumentNullException("functionMetadata");
            }
            if (triggerMetadata == null)
            {
                throw new ArgumentNullException("triggerMetadata");
            }
            if (methodAttributes == null)
            {
                throw new ArgumentNullException("methodAttributes");
            }

            ParameterDescriptor triggerParameter = null;
            switch (triggerMetadata.Type)
            {
                case BindingType.QueueTrigger:
                    triggerParameter = ParseQueueTrigger((QueueBindingMetadata)triggerMetadata);
                    break;
                case BindingType.EventHubTrigger:
                    triggerParameter = ParseEventHubTrigger((EventHubBindingMetadata)triggerMetadata);
                    break;
                case BindingType.BlobTrigger:
                    triggerParameter = ParseBlobTrigger((BlobBindingMetadata)triggerMetadata, typeof(Stream));
                    break;
                case BindingType.ServiceBusTrigger:
                    triggerParameter = ParseServiceBusTrigger((ServiceBusBindingMetadata)triggerMetadata);
                    break;
                case BindingType.TimerTrigger:
                    triggerParameter = ParseTimerTrigger((TimerBindingMetadata)triggerMetadata, typeof(TimerInfo));
                    break;
                case BindingType.HttpTrigger:
                    triggerParameter = ParseHttpTrigger((HttpTriggerBindingMetadata)triggerMetadata, typeof(HttpRequestMessage));
                    break;
                case BindingType.ManualTrigger:
                    triggerParameter = ParseManualTrigger(triggerMetadata);
                    break;
                case BindingType.ApiHubTrigger:
                    triggerParameter = ParseApiHubTrigger((ApiHubBindingMetadata)triggerMetadata, typeof(Stream));
                    break;
            }

            ApplyMethodLevelAttributes(functionMetadata, triggerMetadata, methodAttributes);

            Collection<ParameterDescriptor> parameters = new Collection<ParameterDescriptor>();
            triggerParameter.IsTrigger = true;
            parameters.Add(triggerParameter);

            // Add a TraceWriter for logging
            parameters.Add(new ParameterDescriptor("log", typeof(TraceWriter)));

            // Add an IBinder to support the binding programming model
            parameters.Add(new ParameterDescriptor("binder", typeof(IBinder)));

            // Add ExecutionContext to provide access to InvocationId, etc.
            parameters.Add(new ParameterDescriptor("context", typeof(ExecutionContext)));

            return parameters;
        }

        protected static void ApplyMethodLevelAttributes(FunctionMetadata functionMetadata, BindingMetadata triggerMetadata, Collection<CustomAttributeBuilder> methodAttributes)
        {
            if (functionMetadata.IsDisabled ||
                (triggerMetadata.Type == BindingType.HttpTrigger || triggerMetadata.Type == BindingType.ManualTrigger))
            {
                // the function can be run manually, but there will be no automatic
                // triggering
                ConstructorInfo ctorInfo = typeof(NoAutomaticTriggerAttribute).GetConstructor(new Type[0]);
                CustomAttributeBuilder attributeBuilder = new CustomAttributeBuilder(ctorInfo, new object[0]);
                methodAttributes.Add(attributeBuilder);
            }
        }

        protected abstract IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings);

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

            if (!string.IsNullOrEmpty(trigger.Connection))
            {
                FunctionBinding.AddStorageAccountAttribute(attributes, trigger.Connection);
            }

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

            if (!string.IsNullOrEmpty(trigger.Connection))
            {
                FunctionBinding.AddStorageAccountAttribute(attributes, trigger.Connection);
            }

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

        protected ParameterDescriptor ParseHttpTrigger(HttpTriggerBindingMetadata trigger, Type triggerParameterType = null)
        {
            if (trigger == null)
            {
                throw new ArgumentNullException("trigger");
            }

            if (triggerParameterType == null)
            {
                triggerParameterType = typeof(string);
            }

            return new ParameterDescriptor(trigger.Name, triggerParameterType);
        }

        protected ParameterDescriptor ParseManualTrigger(BindingMetadata trigger, Type triggerParameterType = null)
        {
            if (trigger == null)
            {
                throw new ArgumentNullException("trigger");
            }

            if (triggerParameterType == null)
            {
                triggerParameterType = typeof(string);
            }

            return new ParameterDescriptor(trigger.Name, triggerParameterType);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        protected ParameterDescriptor ParseApiHubTrigger(ApiHubBindingMetadata trigger, Type triggerParameterType = null)
        {
            if (trigger == null)
            {
                throw new ArgumentNullException("trigger");
            }

            if (triggerParameterType == null)
            {
                triggerParameterType = typeof(string);
            }

            ConstructorInfo ctorInfo = typeof(ApiHubFileTriggerAttribute).GetConstructor(new Type[] { typeof(string), typeof(string) });

            CustomAttributeBuilder attributeBuilder = new CustomAttributeBuilder(ctorInfo, new object[] { trigger.Key, trigger.Path });
            string parameterName = trigger.Name;
            var attributes = new Collection<CustomAttributeBuilder>
            {
                attributeBuilder
            };
            return new ParameterDescriptor(parameterName, triggerParameterType, attributes);
        }
    }
}
