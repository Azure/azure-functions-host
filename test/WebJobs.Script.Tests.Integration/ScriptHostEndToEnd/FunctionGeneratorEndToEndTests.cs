// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionGeneratorTests
    {
        private static readonly ScriptSettingsManager SettingsManager = ScriptSettingsManager.Instance;

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
            FunctionDescriptor function = new FunctionDescriptor("TimerFunction", invoker, metadata, parameters, null, null, null);
            Collection<FunctionDescriptor> functions = new Collection<FunctionDescriptor>();
            functions.Add(function);

            // Get the Type Attributes (in this case, a TimeoutAttribute)
            ScriptJobHostOptions scriptConfig = new ScriptJobHostOptions();
            scriptConfig.FunctionTimeout = TimeSpan.FromMinutes(5);
            Collection<CustomAttributeBuilder> typeAttributes = new Collection<CustomAttributeBuilder>();

            // generate the Type
            Type functionType = FunctionGenerator.Generate("TestScriptHost", "TestFunctions", typeAttributes, functions);

            // verify the generated function
            MethodInfo method = functionType.GetMethod("TimerFunction");
            ParameterInfo triggerParameter = method.GetParameters()[0];
            TimerTriggerAttribute triggerAttribute = triggerParameter.GetCustomAttribute<TimerTriggerAttribute>();
            Assert.NotNull(triggerAttribute);

            // start the JobHost which will start running the timer function
            var builder = new HostBuilder()
                .ConfigureWebJobs(b =>
                {
                    b.AddTimers()
                    .AddAzureStorageCoreServices();
                })
                .ConfigureServices(s =>
                {
                    s.AddSingleton<ITypeLocator>(new TestTypeLocator(functionType));
                    s.AddSingleton<ILoggerFactory>(new LoggerFactory());
                });


            using (var host = builder.Build())
            {


                await host.StartAsync();
                await Task.Delay(3000);
                await host.StopAsync();
            }

            // verify our custom invoker was called
            Assert.True(invoker.InvokeCount >= 2);
        }
    }
}
