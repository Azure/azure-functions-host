// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class NodeFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        private ScriptHost _host;
        private readonly ScriptHostConfiguration _config;
        private readonly string _rootPath;

        public NodeFunctionDescriptorProvider(ScriptHost host, ScriptHostConfiguration config)
        {
            _host = host;
            _config = config;
            _rootPath = config.RootPath;
        }

        public override bool TryCreate(FunctionMetadata metadata, out FunctionDescriptor functionDescriptor)
        {
            functionDescriptor = null;

            // name might point to a single file, or a module
            string extension = Path.GetExtension(metadata.Source).ToLower();
            if (!(extension == ".js" || string.IsNullOrEmpty(extension)))
            {
                return false;
            }

            // parse the bindings
            JObject bindings = (JObject)metadata.Configuration["bindings"];
            JArray inputs = (JArray)bindings["input"];
            Collection<Binding> inputBindings = Binding.GetBindings(_config, inputs, FileAccess.Read);

            JArray outputs = (JArray)bindings["output"];
            Collection<Binding> outputBindings = Binding.GetBindings(_config, outputs, FileAccess.Write);

            JObject trigger = (JObject)inputs.FirstOrDefault(p => ((string)p["type"]).ToLowerInvariant().EndsWith("trigger"));

            // A function can be disabled at the trigger or function level
            if (IsDisabled(metadata.Name, trigger) ||
                IsDisabled(metadata.Name, metadata.Configuration))
            {
                return false;
            }

            string triggerType = (string)trigger["type"];
            string triggerParameterName = (string)trigger["name"];
            bool triggerNameSpecified = true;
            if (string.IsNullOrEmpty(triggerParameterName))
            {
                // default the name to simply 'input'
                trigger["name"] = triggerParameterName = "input";
                triggerNameSpecified = false;
            }

            Collection<CustomAttributeBuilder> methodAttributes = new Collection<CustomAttributeBuilder>();
            ParameterDescriptor triggerParameter = null;
            switch (triggerType)
            {
                case "queueTrigger":
                    triggerParameter = ParseQueueTrigger(trigger);
                    break;
                case "blobTrigger":
                    triggerParameter = ParseBlobTrigger(trigger);
                    break;
                case "serviceBusTrigger":
                    triggerParameter = ParseServiceBusTrigger(trigger);
                    break;
                case "timerTrigger":
                    triggerParameter = ParseTimerTrigger(trigger, typeof(TimerInfo));
                    break;
                case "httpTrigger":
                    if (!triggerNameSpecified)
                    {
                        trigger["name"] = triggerParameterName = "req";
                    }
                    triggerParameter = ParseHttpTrigger(trigger, methodAttributes, typeof(HttpRequestMessage));
                    break;
            }

            Collection<ParameterDescriptor> parameters = new Collection<ParameterDescriptor>();
            parameters.Add(triggerParameter);

            // Add a TraceWriter for logging
            parameters.Add(new ParameterDescriptor("log", typeof(TraceWriter)));

            // Add an IBinder to support the binding programming model
            parameters.Add(new ParameterDescriptor("binder", typeof(IBinder)));

            NodeFunctionInvoker invoker = new NodeFunctionInvoker(_host, triggerParameterName, metadata, inputBindings, outputBindings);
            functionDescriptor = new FunctionDescriptor(metadata.Name, invoker, metadata, parameters, methodAttributes);

            return true;
        }
    }
}
