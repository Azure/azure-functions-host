// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Timers;
using Microsoft.Azure.WebJobs.Extensions.WebHooks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json.Linq;
using Xunit;

namespace WebJobs.Script.Tests
{
    public class CSharpFunctionGenerationTests
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
                { "source", "TimerTrigger.cs" },
                { "trigger", trigger },
            };
            MethodInfo method = GenerateMethod(function);

            Assert.Equal("TimerTrigger", method.Name);
            ParameterInfo[] parameters = method.GetParameters();
            Assert.Equal(1, parameters.Length);
            Assert.Equal(typeof(Task), method.ReturnType);

            // verify trigger parameter
            ParameterInfo parameter = parameters[0];
            Assert.Equal("timerInfo", parameter.Name);
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
                { "type", "queue" },
                { "queueName", "test" }
            };
            JObject function = new JObject
            {
                { "source", "QueueTrigger.cs" },
                { "trigger", trigger },
            };
            MethodInfo method = GenerateMethod(function);

            Assert.Equal("QueueTrigger", method.Name);
            ParameterInfo[] parameters = method.GetParameters();
            Assert.Equal(2, parameters.Length);
            Assert.Equal(typeof(Task), method.ReturnType);

            // verify trigger parameter
            ParameterInfo parameter = method.GetParameters()[0];
            Assert.Equal("message", parameter.Name);
            Assert.Equal(typeof(TestPoco), parameter.ParameterType);
            QueueTriggerAttribute attribute = parameter.GetCustomAttribute<QueueTriggerAttribute>();
            Assert.Equal("test", attribute.QueueName);

            // verify TraceWriter parameter
            parameter = parameters[1];
            Assert.Equal("traceWriter", parameter.Name);
            Assert.Equal(typeof(TraceWriter), parameter.ParameterType); 
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
                { "source", "BlobTrigger.cs" },
                { "trigger", trigger },
            };
            MethodInfo method = GenerateMethod(function);

            Assert.Equal("BlobTrigger", method.Name);
            ParameterInfo[] parameters = method.GetParameters();
            Assert.Equal(1, parameters.Length);
            Assert.Equal(typeof(Task), method.ReturnType);

            // verify trigger parameter
            ParameterInfo parameter = method.GetParameters()[0];
            Assert.Equal("blob", parameter.Name);
            Assert.Equal(typeof(string), parameter.ParameterType);
            BlobTriggerAttribute attribute = parameter.GetCustomAttribute<BlobTriggerAttribute>();
            Assert.Equal("foo/bar", attribute.BlobPath);
        }

        [Fact]
        public void GenerateWebHookTriggerFunction()
        {
            JObject trigger = new JObject
            {
                { "type", "webHook" },
                { "route", "foo/bar" }
            };
            JObject function = new JObject
            {
                { "source", "WebHookTrigger.cs" },
                { "trigger", trigger },
            };
            MethodInfo method = GenerateMethod(function);

            Assert.Equal("WebHookTrigger", method.Name);
            ParameterInfo[] parameters = method.GetParameters();
            Assert.Equal(1, parameters.Length);
            Assert.Equal(typeof(Task), method.ReturnType);

            // verify trigger parameter
            ParameterInfo parameter = method.GetParameters()[0];
            Assert.Equal("request", parameter.Name);
            Assert.Equal(typeof(HttpRequestMessage), parameter.ParameterType);
            WebHookTriggerAttribute attribute = parameter.GetCustomAttribute<WebHookTriggerAttribute>();
            Assert.Equal("foo/bar", attribute.Route);
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
                { "source", "ServiceBusTrigger.cs" },
                { "trigger", trigger },
            };
            MethodInfo method = GenerateMethod(function);

            Assert.Equal("ServiceBusTrigger", method.Name);
            ParameterInfo[] parameters = method.GetParameters();
            Assert.Equal(1, parameters.Length);
            Assert.Equal(typeof(Task), method.ReturnType);

            // verify trigger parameter
            ParameterInfo parameter = method.GetParameters()[0];
            Assert.Equal("message", parameter.Name);
            Assert.Equal(typeof(BrokeredMessage), parameter.ParameterType);
            ServiceBusTriggerAttribute attribute = parameter.GetCustomAttribute<ServiceBusTriggerAttribute>();
            Assert.Equal(null, attribute.QueueName);
            Assert.Equal("testTopic", attribute.TopicName);
            Assert.Equal("testSubscription", attribute.SubscriptionName);
            Assert.Equal(AccessRights.Listen, attribute.Access);
        }

        private static MethodInfo GenerateMethod(JObject function)
        {
            JObject manifest = new JObject
            {
                { "functions", new JArray(function) }
            };

            FunctionDescriptorProvider[] descriptorProviders = new FunctionDescriptorProvider[]
            {
                new CSharpFunctionDescriptorProvider(new Type[] { typeof(Functions) })
            };
            var functions = Manifest.ReadFunctions(manifest, descriptorProviders);
            Type t = FunctionGenerator.Generate("Host.Functions", functions);

            MethodInfo method = t.GetMethods(BindingFlags.Public | BindingFlags.Static).First();
            return method;
        }

        public class TestPoco
        {
            public string A { get; set; }
            public string B { get; set; }
        }

        public static class Functions
        {
            public static Task QueueTrigger(TestPoco message, TraceWriter traceWriter)
            {
                return Task.FromResult(0);
            }

            public static Task TimerTrigger(TimerInfo timerInfo)
            {
                return Task.FromResult(0);
            }

            public static Task WebHookTrigger(HttpRequestMessage request)
            {
                return Task.FromResult(0);
            }

            public static Task ServiceBusTrigger(BrokeredMessage message)
            {
                return Task.FromResult(0);
            }

            public static void BlobTrigger(string blob)
            {
            }
        }
    }
}
