// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Reflection;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.WindowsAzure.Storage.Queue;
using Xunit;

namespace WebJobs.Script.Tests
{
    public class FunctionDescriptorTests
    {
        [Fact]
        public void FromMethodDefinition()
        {
            MethodInfo method = GetType().GetMethod("ProcessMessage", BindingFlags.NonPublic | BindingFlags.Static);
            TestInvoker invoker = new TestInvoker();
            FunctionDescriptor function = FunctionDescriptor.FromMethod(method, invoker);

            Assert.Equal(1, function.Parameters.Count);
            Assert.Equal(1, function.CustomAttributes.Count);
            Assert.Equal("ProcessMessage", function.Name);

            ParameterDescriptor parameter = function.Parameters[0];
            Assert.Equal("message", parameter.Name);
            Assert.Equal(typeof(CloudQueueMessage), parameter.Type);
            Assert.Equal(1, parameter.CustomAttributes.Count);

            Collection<FunctionDescriptor> functions = new Collection<FunctionDescriptor>();
            functions.Add(function);
            Type type = FunctionGenerator.Generate("TestScriptHost", "TestFunctions", functions);

            MethodInfo result = type.GetMethod("ProcessMessage");
            SingletonAttribute singleton = result.GetCustomAttribute<SingletonAttribute>();
            Assert.Equal("testscope", singleton.Scope);
            QueueTriggerAttribute queueAttribute = result.GetParameters()[0].GetCustomAttribute<QueueTriggerAttribute>();
            Assert.Equal("testqueue", queueAttribute.QueueName);
        }

        [Singleton("testscope")]
        private static void ProcessMessage([QueueTrigger("testqueue")] CloudQueueMessage message) { }
    }
}
