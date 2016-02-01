// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json.Linq;
using Xunit;

namespace WebJobs.Script.Tests
{
    public class NodeFunctionGenerationTests
    {
        [Fact]
        public void GenerateTimerTriggerFunction()
        {
            JObject trigger = new JObject
            {
                { "type", "timerTrigger" },
                { "schedule", "* * * * * *" },
                { "runOnStartup", true }
            };
            MethodInfo method = GenerateMethod(trigger);

            VerifyCommonProperties(method);

            // verify trigger parameter
            ParameterInfo parameter = method.GetParameters()[0];
            Assert.Equal("input", parameter.Name);
            Assert.Equal(typeof(TimerInfo), parameter.ParameterType);
            TimerTriggerAttribute attribute = parameter.GetCustomAttribute<TimerTriggerAttribute>();
            Assert.Equal("Cron: '* * * * * *'", attribute.Schedule.ToString());
            Assert.True(attribute.RunOnStartup);
        }

        [Fact]
        public void GenerateQueueTriggerFunction()
        {
            JObject trigger = new JObject
            {
                { "type", "queueTrigger" },
                { "queueName", "test" }
            };
            MethodInfo method = GenerateMethod(trigger);

            VerifyCommonProperties(method);

            // verify trigger parameter
            ParameterInfo parameter = method.GetParameters()[0];
            Assert.Equal("input", parameter.Name);
            Assert.Equal(typeof(string), parameter.ParameterType);
            QueueTriggerAttribute attribute = parameter.GetCustomAttribute<QueueTriggerAttribute>();
            Assert.Equal("test", attribute.QueueName);
        }

        [Fact]
        public void GenerateBlobTriggerFunction()
        {
            JObject trigger = new JObject
            {
                { "type", "blobTrigger" },
                { "path", "foo/bar" }
            };
            MethodInfo method = GenerateMethod(trigger);

            VerifyCommonProperties(method);

            // verify trigger parameter
            ParameterInfo parameter = method.GetParameters()[0];
            Assert.Equal("input", parameter.Name);
            Assert.Equal(typeof(string), parameter.ParameterType);
            BlobTriggerAttribute attribute = parameter.GetCustomAttribute<BlobTriggerAttribute>();
            Assert.Equal("foo/bar", attribute.BlobPath);
        }

        [Fact]
        public void GenerateHttpTriggerFunction()
        {
            JObject trigger = new JObject
            {
                { "type", "httpTrigger" }
            };
            MethodInfo method = GenerateMethod(trigger);

            VerifyCommonProperties(method);

            // verify trigger parameter
            ParameterInfo parameter = method.GetParameters()[0];
            Assert.Equal("req", parameter.Name);
            Assert.Equal(typeof(HttpRequestMessage), parameter.ParameterType);
            NoAutomaticTriggerAttribute attribute = method.GetCustomAttribute<NoAutomaticTriggerAttribute>();
            Assert.NotNull(attribute);
        }

        [Fact]
        public void GenerateManualTriggerFunction()
        {
            JObject trigger = new JObject
            {
                { "type", "manualTrigger" }
            };
            MethodInfo method = GenerateMethod(trigger);

            VerifyCommonProperties(method);

            // verify trigger parameter
            ParameterInfo parameter = method.GetParameters()[0];
            Assert.Equal("input", parameter.Name);
            Assert.Equal(typeof(string), parameter.ParameterType);
            NoAutomaticTriggerAttribute attribute = method.GetCustomAttribute<NoAutomaticTriggerAttribute>();
            Assert.NotNull(attribute);
        }

        [Fact]
        public void GenerateServiceBusTriggerFunction()
        {
            JObject trigger = new JObject
            {
                { "type", "serviceBusTrigger" },
                { "topicName", "testTopic" },
                { "subscriptionName", "testSubscription" },
                { "accessRights", "listen" }
            };
            MethodInfo method = GenerateMethod(trigger);

            VerifyCommonProperties(method);

            // verify trigger parameter
            ParameterInfo parameter = method.GetParameters()[0];
            Assert.Equal("input", parameter.Name);
            Assert.Equal(typeof(string), parameter.ParameterType);
            ServiceBusTriggerAttribute attribute = parameter.GetCustomAttribute<ServiceBusTriggerAttribute>();
            Assert.Equal(null, attribute.QueueName);
            Assert.Equal("testTopic", attribute.TopicName);
            Assert.Equal("testSubscription", attribute.SubscriptionName);
            Assert.Equal(AccessRights.Listen, attribute.Access);
        }

        private static void VerifyCommonProperties(MethodInfo method)
        {
            Assert.Equal("Test", method.Name);
            ParameterInfo[] parameters = method.GetParameters();
            Assert.Equal(4, parameters.Length);
            Assert.Equal(typeof(Task), method.ReturnType);

            // verify TextWriter parameter
            ParameterInfo parameter = parameters[1];
            Assert.Equal("log", parameter.Name);
            Assert.Equal(typeof(TraceWriter), parameter.ParameterType);

            // verify IBinder parameter
            parameter = parameters[2];
            Assert.Equal("binder", parameter.Name);
            Assert.Equal(typeof(IBinder), parameter.ParameterType);

            // verify ExecutionContext parameter
            parameter = parameters[3];
            Assert.Equal("context", parameter.Name);
            Assert.Equal(typeof(ExecutionContext), parameter.ParameterType);
        }

        private static MethodInfo GenerateMethod(JObject trigger)
        {
            string rootPath = Path.Combine(Environment.CurrentDirectory, @"TestScripts");
            FunctionMetadata metadata = new FunctionMetadata();
            metadata.Name = "Test";
            metadata.Source = Path.Combine(rootPath, @"Node\Common\test.js");

            JArray inputs = new JArray(trigger);
            metadata.Configuration = new JObject();
            metadata.Configuration["bindings"] = new JObject();
            metadata.Configuration["bindings"]["input"] = inputs;

            List<FunctionMetadata> metadatas = new List<FunctionMetadata>();
            metadatas.Add(metadata);

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration()
            {
                RootScriptPath = rootPath
            };
            ScriptHost host = ScriptHost.Create(scriptConfig);
            FunctionDescriptorProvider[] descriptorProviders = new FunctionDescriptorProvider[]
            {
                new NodeFunctionDescriptorProvider(host, scriptConfig)
            };
            var functionDescriptors = ScriptHost.ReadFunctions(metadatas, descriptorProviders);
            Type t = FunctionGenerator.Generate("TestScriptHost", "Host.Functions", functionDescriptors);

            MethodInfo method = t.GetMethods(BindingFlags.Public | BindingFlags.Static).First();
            return method;
        }
    }
}
