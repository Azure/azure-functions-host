// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Timers;
using Microsoft.Azure.WebJobs.Extensions.WebHooks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Node;
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
                { "type", "timer" },
                { "schedule", "* * * * * *" },
                { "runOnStartup", true }
            };
            JObject function = new JObject
            {
                { "source", "test.js" },
                { "trigger", trigger },
            };
            MethodInfo method = GenerateMethod(function);

            Assert.Equal("Test", method.Name);
            ParameterInfo[] parameters = method.GetParameters();
            Assert.Equal(2, parameters.Length);
            Assert.Equal(typeof(Task), method.ReturnType);

            // verify trigger parameter
            ParameterInfo parameter = parameters[0];
            Assert.Equal("input", parameter.Name);
            Assert.Equal(typeof(TimerInfo), parameter.ParameterType);
            TimerTriggerAttribute attribute = parameter.GetCustomAttribute<TimerTriggerAttribute>();
            Assert.Equal("Cron: '* * * * * *'", attribute.Schedule.ToString());
            Assert.True(attribute.RunOnStartup);

            // verify TextWriter parameter
            parameter = parameters[1];
            Assert.Equal("log", parameter.Name);
            Assert.Equal(typeof(TextWriter), parameter.ParameterType);
        }

        [Fact]
        public void GenerateQueueTriggerFunction()
        {
            JObject trigger = new JObject
            {
                { "type", "queue" },
                { "queueName", "test" }
            };
            JObject function = new JObject
            {
                { "name", "Foo" },
                { "source", "test.js" },
                { "trigger", trigger },
            };
            MethodInfo method = GenerateMethod(function);

            VerifyCommonProperties(method, "Foo");

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
                { "type", "blob" },
                { "blobPath", "foo/bar" }
            };
            JObject function = new JObject
            {
                { "source", "test.js" },
                { "trigger", trigger },
            };
            MethodInfo method = GenerateMethod(function);

            VerifyCommonProperties(method);

            // verify trigger parameter
            ParameterInfo parameter = method.GetParameters()[0];
            Assert.Equal("input", parameter.Name);
            Assert.Equal(typeof(string), parameter.ParameterType);
            BlobTriggerAttribute attribute = parameter.GetCustomAttribute<BlobTriggerAttribute>();
            Assert.Equal("foo/bar", attribute.BlobPath);
        }

        [Fact]
        public void GenerateWebHookTriggerFunction()
        {
            JObject trigger = new JObject
            {
                { "type", "webHook" }
            };
            JObject function = new JObject
            {
                { "source", "test.js" },
                { "trigger", trigger },
            };
            MethodInfo method = GenerateMethod(function);

            VerifyCommonProperties(method);

            // verify trigger parameter
            ParameterInfo parameter = method.GetParameters()[0];
            Assert.Equal("input", parameter.Name);
            Assert.Equal(typeof(string), parameter.ParameterType);
            WebHookTriggerAttribute attribute = parameter.GetCustomAttribute<WebHookTriggerAttribute>();
            Assert.NotNull(attribute);
        }

        [Fact]
        public void GenerateServiceBusTriggerFunction()
        {
            JObject trigger = new JObject
            {
                { "type", "serviceBus" },
                { "topicName", "testTopic" },
                { "subscriptionName", "testSubscription" },
                { "accessRights", "listen" }
            };
            JObject function = new JObject
            {
                { "source", "test.js" },
                { "trigger", trigger },
            };
            MethodInfo method = GenerateMethod(function);

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

        private static void VerifyCommonProperties(MethodInfo method, string expectedName = "Test")
        {
            Assert.Equal(expectedName, method.Name);
            ParameterInfo[] parameters = method.GetParameters();
            Assert.Equal(2, parameters.Length);
            Assert.Equal(typeof(Task), method.ReturnType);

            ParameterInfo parameter = parameters[0];
            Assert.Equal("input", parameter.Name);
            Assert.Equal(typeof(string), parameter.ParameterType);

            // verify TextWriter parameter
            parameter = parameters[1];
            Assert.Equal("log", parameter.Name);
            Assert.Equal(typeof(TextWriter), parameter.ParameterType);
        }

        private static MethodInfo GenerateMethod(JObject function)
        {
            JObject manifest = new JObject
            {
                { "functions", new JArray(function) }
            };

            string applicationRootPath = Path.Combine(Environment.CurrentDirectory, "node");
            FunctionDescriptorProvider[] descriptorProviders = new FunctionDescriptorProvider[]
            {
                new NodeFunctionDescriptorProvider(applicationRootPath)
            };
            var functions = Manifest.ReadFunctions(manifest, descriptorProviders);
            Type t = FunctionGenerator.Generate(functions);

            MethodInfo method = t.GetMethods(BindingFlags.Public | BindingFlags.Static).First();
            return method;
        }
    }
}
