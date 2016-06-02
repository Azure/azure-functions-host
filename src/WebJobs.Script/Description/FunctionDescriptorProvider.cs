// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.ServiceBus.Messaging;
using NCrontab;

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

            ValidateFunction(functionMetadata);

            // parse the bindings
            Collection<FunctionBinding> inputBindings = FunctionBinding.GetBindings(Config, functionMetadata.InputBindings, FileAccess.Read);
            Collection<FunctionBinding> outputBindings = FunctionBinding.GetBindings(Config, functionMetadata.OutputBindings, FileAccess.Write);

            BindingMetadata triggerMetadata = functionMetadata.InputBindings.FirstOrDefault(p => p.IsTrigger);
            string scriptFilePath = Path.Combine(Config.RootScriptPath, functionMetadata.ScriptFile);
            functionDescriptor = null;
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

            ApplyMethodLevelAttributes(functionMetadata, triggerMetadata, methodAttributes);

            Collection<ParameterDescriptor> parameters = new Collection<ParameterDescriptor>();
            ParameterDescriptor triggerParameter = CreateTriggerParameter(triggerMetadata);
            parameters.Add(triggerParameter);

            // Add a TraceWriter for logging
            parameters.Add(new ParameterDescriptor("log", typeof(TraceWriter)));

            // Add an IBinder to support the binding programming model
            parameters.Add(new ParameterDescriptor("binder", typeof(IBinder)));

            // Add ExecutionContext to provide access to InvocationId, etc.
            parameters.Add(new ParameterDescriptor("context", typeof(ExecutionContext)));

            return parameters;
        }

        protected virtual ParameterDescriptor CreateTriggerParameter(BindingMetadata triggerMetadata, Type parameterType = null)
        {
            ParameterDescriptor triggerParameter = null;
            switch (triggerMetadata.Type)
            {
                case BindingType.QueueTrigger:
                    parameterType = DefaultParameterType(parameterType, triggerMetadata.DataType);
                    triggerParameter = ParseQueueTrigger((QueueBindingMetadata)triggerMetadata, parameterType);
                    break;
                case BindingType.EventHubTrigger:
                    parameterType = DefaultParameterType(parameterType, triggerMetadata.DataType);
                    triggerParameter = ParseEventHubTrigger((EventHubBindingMetadata)triggerMetadata, parameterType);
                    break;
                case BindingType.BlobTrigger:
                    triggerParameter = ParseBlobTrigger((BlobBindingMetadata)triggerMetadata, parameterType ?? typeof(Stream));
                    break;
                case BindingType.ServiceBusTrigger:
                    parameterType = DefaultParameterType(parameterType, triggerMetadata.DataType);
                    triggerParameter = ParseServiceBusTrigger((ServiceBusBindingMetadata)triggerMetadata, parameterType);
                    break;
                case BindingType.TimerTrigger:
                    triggerParameter = ParseTimerTrigger((TimerBindingMetadata)triggerMetadata, parameterType ?? typeof(TimerInfo));
                    break;
                case BindingType.HttpTrigger:
                    triggerParameter = ParseHttpTrigger((HttpTriggerBindingMetadata)triggerMetadata, parameterType ?? typeof(HttpRequestMessage));
                    break;
                case BindingType.ManualTrigger:
                    triggerParameter = ParseManualTrigger(triggerMetadata, parameterType ?? typeof(string));
                    break;
                case BindingType.ApiHubFileTrigger:
                    triggerParameter = ParseApiHubFileTrigger((ApiHubBindingMetadata)triggerMetadata, parameterType ?? typeof(Stream));
                    break;
            }

            triggerParameter.IsTrigger = true;

            return triggerParameter;
        }

        protected internal virtual void ValidateFunction(FunctionMetadata functionMetadata)
        {
            // Functions must have a trigger binding
            var triggerMetadata = functionMetadata.InputBindings.FirstOrDefault(p => p.IsTrigger);
            if (triggerMetadata == null)
            {
                throw new InvalidOperationException("No trigger binding specified. A function must have a trigger input binding.");
            }

            HashSet<string> names = new HashSet<string>();
            foreach (var binding in functionMetadata.Bindings)
            {
                ValidateBinding(binding);

                // Ensure no duplicate binding names
                if (names.Contains(binding.Name))
                {
                    throw new InvalidOperationException(string.Format("Multiple bindings with name '{0}' discovered. Binding names must be unique.", binding.Name));
                }
                else
                {
                    names.Add(binding.Name);
                }
            }
        }

        protected internal virtual void ValidateBinding(BindingMetadata bindingMetadata)
        {
            if (string.IsNullOrEmpty(bindingMetadata.Name))
            {
                throw new ArgumentException("A valid name must be assigned to the binding.");
            }
        }

        /// <summary>
        /// If the specified parameter Type is null, attempt to default it based on
        /// the DataType value specified.
        /// </summary>
        private static Type DefaultParameterType(Type parameterType, DataType? dataType)
        {
            if (parameterType == null)
            {
                parameterType = dataType == DataType.Binary ? typeof(byte[]) : typeof(string);
            }

            return parameterType;
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

        protected ParameterDescriptor ParseEventHubTrigger(EventHubBindingMetadata trigger, Type triggerParameterType)
        {
            if (trigger == null)
            {
                throw new ArgumentNullException("trigger");
            }
            if (triggerParameterType == null)
            {
                throw new ArgumentNullException("triggerParameterType");
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

        protected ParameterDescriptor ParseQueueTrigger(QueueBindingMetadata trigger, Type triggerParameterType)
        {
            if (trigger == null)
            {
                throw new ArgumentNullException("trigger");
            }
            if (triggerParameterType == null)
            {
                throw new ArgumentNullException("triggerParameterType");
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

        protected ParameterDescriptor ParseBlobTrigger(BlobBindingMetadata trigger, Type triggerParameterType)
        {
            if (trigger == null)
            {
                throw new ArgumentNullException("trigger");
            }

            if (triggerParameterType == null)
            {
                throw new ArgumentNullException("triggerParameterType");
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

        protected ParameterDescriptor ParseServiceBusTrigger(ServiceBusBindingMetadata trigger, Type triggerParameterType)
        {
            if (trigger == null)
            {
                throw new ArgumentNullException("trigger");
            }

            if (triggerParameterType == null)
            {
                throw new ArgumentNullException("triggerParameterType");
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

            var attributes = new Collection<CustomAttributeBuilder>
            {
                attributeBuilder
            };

            if (!string.IsNullOrEmpty(trigger.Connection))
            {
                ServiceBusBinding.AddServiceBusAccountAttribute(attributes, trigger.Connection);
            }

            return new ParameterDescriptor(trigger.Name, triggerParameterType, attributes);
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

            CrontabSchedule.ParseOptions options = new CrontabSchedule.ParseOptions()
            {
                IncludingSeconds = true
            };
            if (CrontabSchedule.TryParse(trigger.Schedule, options) == null)
            {
                throw new ArgumentException(string.Format("'{0}' is not a valid CRON expression.", trigger.Schedule));
            }

            ConstructorInfo ctorInfo = typeof(TimerTriggerAttribute).GetConstructor(new Type[] { typeof(string) });
            
            PropertyInfo runOnStartupProperty = typeof(TimerTriggerAttribute).GetProperty("RunOnStartup");
            CustomAttributeBuilder attributeBuilder = new CustomAttributeBuilder(ctorInfo, 
                new object[] { trigger.Schedule }, 
                new PropertyInfo[] { runOnStartupProperty }, 
                new object[] { trigger.RunOnStartup });

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

        protected ParameterDescriptor ParseApiHubFileTrigger(ApiHubBindingMetadata trigger, Type triggerParameterType = null)
        {
            if (trigger == null)
            {
                throw new ArgumentNullException("trigger");
            }

            if (triggerParameterType == null)
            {
                triggerParameterType = typeof(string);
            }

            ConstructorInfo ctorInfo = typeof(ApiHubFileTriggerAttribute).GetConstructor(new Type[] { typeof(string), typeof(string), typeof(ApiHub.FileWatcherType), typeof(int) });

            CustomAttributeBuilder attributeBuilder = new CustomAttributeBuilder(ctorInfo, new object[] { trigger.Key, trigger.Path, trigger.FileWatcherType, trigger.PollIntervalInSeconds });

            string parameterName = trigger.Name;
            var attributes = new Collection<CustomAttributeBuilder>
            {
                attributeBuilder
            };
            return new ParameterDescriptor(parameterName, triggerParameterType, attributes);
        }
    }
}
