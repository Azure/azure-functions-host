// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Xunit;

namespace WebJobs.Script.Tests
{
    [Trait("Category", "E2E")]
    public class FunctionGeneratorTests
    {
        [Fact]
        public async Task Generate_EndToEnd()
        {
            // construct our TimerTrigger attribute ([TimerTrigger("00:00:02", RunOnStartup = true)])
            Collection<ParameterDescriptor> parameters = new Collection<ParameterDescriptor>();
            ParameterDescriptor parameter = new ParameterDescriptor("timerInfo", typeof(TimerInfo));
            ConstructorInfo ctorInfo = typeof(TimerTriggerAttribute).GetConstructor(new Type[] { typeof(string) });
            PropertyInfo runOnStartupProperty = typeof(TimerTriggerAttribute).GetProperty("RunOnStartup");
            CustomAttributeBuilder attributeBuilder = new CustomAttributeBuilder(
                ctorInfo,
                new object[] { "00:00:02" },
                new PropertyInfo[] { runOnStartupProperty },
                new object[] { true });
            parameter.CustomAttributes.Add(attributeBuilder);
            parameters.Add(parameter);

            // create the FunctionDefinition
            FunctionMetadata metadata = new FunctionMetadata();
            TestInvoker invoker = new TestInvoker();
            FunctionDescriptor function = new FunctionDescriptor("TimerFunction", invoker, metadata, parameters);
            Collection<FunctionDescriptor> functions = new Collection<FunctionDescriptor>();
            functions.Add(function);

            // generate the Type
            Type functionType = FunctionGenerator.Generate("TestScriptHost", "TestFunctions", functions);

            // verify the generated function
            MethodInfo method = functionType.GetMethod("TimerFunction");
            ParameterInfo triggerParameter = method.GetParameters()[0];
            TimerTriggerAttribute triggerAttribute = triggerParameter.GetCustomAttribute<TimerTriggerAttribute>();
            Assert.NotNull(triggerAttribute);

            // start the JobHost which will start running the timer function
            JobHostConfiguration config = new JobHostConfiguration()
            {
                TypeLocator = new TypeLocator(functionType)
            };
            config.UseTimers();
            JobHost host = new JobHost(config);

            await host.StartAsync();
            await Task.Delay(3000);
            await host.StopAsync();

            // verify our custom invoker was called
            Assert.True(invoker.InvokeCount >= 2);
        }

        [Fact]
        public void Generate_WithMultipleOutParameters()
        {
            string functionName = "FunctionWithOuts";
            Collection<ParameterDescriptor> parameters = new Collection<ParameterDescriptor>();

            parameters.Add(new ParameterDescriptor("param1", typeof(string)));
            parameters.Add(new ParameterDescriptor("param2", typeof(string).MakeByRefType()) { Attributes = ParameterAttributes.Out });
            parameters.Add(new ParameterDescriptor("param3", typeof(string).MakeByRefType()) { Attributes = ParameterAttributes.Out });

            FunctionMetadata metadata = new FunctionMetadata();
            TestInvoker invoker = new TestInvoker();
            FunctionDescriptor function = new FunctionDescriptor(functionName, invoker, metadata, parameters);
            Collection<FunctionDescriptor> functions = new Collection<FunctionDescriptor>();
            functions.Add(function);

            // generate the Type
            Type functionType = FunctionGenerator.Generate("TestScriptHost", "TestFunctions", functions);

            // verify the generated function
            MethodInfo method = functionType.GetMethod(functionName);
            ParameterInfo[] functionParams = method.GetParameters();

            // Verify that we have the correct number of parameters
            Assert.Equal(parameters.Count, functionParams.Length);

            // Verify that out parameters were correctly generated
            Assert.True(functionParams[1].IsOut);
            Assert.True(functionParams[2].IsOut);

            // Verify that the method is invocable
            method.Invoke(null, new object[] { "test", null, null });

            // verify our custom invoker was called
            Assert.Equal(1, invoker.InvokeCount);
        }
    }
}
