// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class NodeFunctionGenerationTests
    {
        [Fact]
        public void GenerateTimerTriggerFunction()
        {
            BindingMetadata trigger = BindingMetadata.Create(new JObject
            {
                { "type", "TimerTrigger" },
                { "name", "timerInfo" },
                { "schedule", "* * * * * *" },
                { "runOnStartup", true },
                { "direction", "in" }
            });
            MethodInfo method = GenerateMethod(trigger);

            VerifyCommonProperties(method);

            // verify trigger parameter
            ParameterInfo parameter = method.GetParameters()[0];
            Assert.Equal("timerInfo", parameter.Name);
            Assert.Equal(typeof(TimerInfo), parameter.ParameterType);
            TimerTriggerAttribute attribute = parameter.GetCustomAttribute<TimerTriggerAttribute>();
            Assert.Equal("* * * * * *", attribute.ScheduleExpression);
            Assert.True(attribute.RunOnStartup);
        }

        [Fact]
        public void GenerateQueueTriggerFunction()
        {
            BindingMetadata trigger = BindingMetadata.Create(new JObject
            {
                { "type", "QueueTrigger" },
                { "name", "input" },
                { "direction", "in" },
                { "queueName", "test" }
            });
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
            BindingMetadata trigger = BindingMetadata.Create(new JObject
            {
                { "type", "blobTrigger" },
                { "name", "input" },
                { "direction", "in" },
                { "path", "foo/bar" }
            });
            MethodInfo method = GenerateMethod(trigger);

            VerifyCommonProperties(method);

            // verify trigger parameter
            ParameterInfo parameter = method.GetParameters()[0];
            Assert.Equal("input", parameter.Name);
            Assert.Equal(typeof(Stream), parameter.ParameterType);
            BlobTriggerAttribute attribute = parameter.GetCustomAttribute<BlobTriggerAttribute>();
            Assert.Equal("foo/bar", attribute.BlobPath);
        }

        [Fact]
        public void GenerateHttpTriggerFunction()
        {
            BindingMetadata trigger = BindingMetadata.Create(new JObject
            {
                { "type", "HttpTrigger" },
                { "name", "req" }
            });
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
            BindingMetadata trigger = BindingMetadata.Create(new JObject
            {
                { "type", "ManualTrigger" },
                { "name", "input" },
                { "direction", "in" }
            });
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
            BindingMetadata trigger = BindingMetadata.Create(new JObject
            {
                { "type", "ServiceBusTrigger" },
                { "name", "input" },
                { "direction", "in" },
                { "topicName", "testTopic" },
                { "subscriptionName", "testSubscription" },
                { "accessRights", "Listen" }
            });
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

        private static MethodInfo GenerateMethod(BindingMetadata trigger)
        {
            string rootPath = Path.Combine(Environment.CurrentDirectory, @"TestScripts\Node");
            FunctionMetadata metadata = new FunctionMetadata();
            metadata.Name = "Test";
            metadata.ScriptFile = Path.Combine(rootPath, @"Common\test.js");
            metadata.Bindings.Add(trigger);

            List<FunctionMetadata> metadatas = new List<FunctionMetadata>();
            metadatas.Add(metadata);

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration()
            {
                RootScriptPath = rootPath
            };

            Collection<FunctionDescriptor> functionDescriptors = null;
            using (ScriptHost host = ScriptHost.Create(scriptConfig))
            {
                FunctionDescriptorProvider[] descriptorProviders = new FunctionDescriptorProvider[]
                {
                new NodeFunctionDescriptorProvider(host, scriptConfig)
                };

                functionDescriptors = host.ReadFunctions(metadatas, descriptorProviders);
            }

            Type t = FunctionGenerator.Generate("TestScriptHost", "Host.Functions", functionDescriptors);

            MethodInfo method = t.GetMethods(BindingFlags.Public | BindingFlags.Static).First();
            return method;
        }
    }
}
