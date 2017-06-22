// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.WebJobs.Script.Tests;
using Moq;
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
            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();
            scriptConfig.FunctionTimeout = TimeSpan.FromMinutes(5);
            Collection<CustomAttributeBuilder> typeAttributes = ScriptHost.CreateTypeAttributes(scriptConfig);

            // generate the Type
            Type functionType = FunctionGenerator.Generate("TestScriptHost", "TestFunctions", typeAttributes, functions);

            // verify the generated function
            MethodInfo method = functionType.GetMethod("TimerFunction");
            TimeoutAttribute timeoutAttribute = (TimeoutAttribute)functionType.GetCustomAttributes().Single();
            Assert.Equal(TimeSpan.FromMinutes(5), timeoutAttribute.Timeout);
            Assert.True(timeoutAttribute.ThrowOnTimeout);
            Assert.True(timeoutAttribute.TimeoutWhileDebugging);
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

        // TODO: FACAVAL - Dependency on the previous HTTP Function lookup/invocation
        //[Fact]
        //public async Task GeneratedMethods_WithOutParams_DoNotCauseDeadlocks_CSharp()
        //{
        //    await GeneratedMethods_WithOutParams_DoNotCauseDeadlocks("csharp");
        //}

        //[Fact]
        //public async Task GeneratedMethods_WithOutParams_DoNotCauseDeadlocks_FSharp()
        //{
        //    await GeneratedMethods_WithOutParams_DoNotCauseDeadlocks("fsharp");
        //}

        //internal async Task GeneratedMethods_WithOutParams_DoNotCauseDeadlocks(string fixture)
        //{
        //    var traceWriter = new TestTraceWriter(TraceLevel.Verbose);

        //    ScriptHostConfiguration config = new ScriptHostConfiguration()
        //    {
        //        RootScriptPath = @"TestScripts\FunctionGeneration",
        //        TraceWriter = traceWriter
        //    };

        //    string secretsPath = Path.Combine(Path.GetTempPath(), @"FunctionTests\Secrets");
        //    ISecretsRepository repository = new FileSystemSecretsRepository(secretsPath);
        //    WebHostSettings webHostSettings = new WebHostSettings();
        //    webHostSettings.SecretsPath = secretsPath;
        //    var eventManagerMock = new Mock<IScriptEventManager>();
        //    var routerMock = new Mock<IWebJobsRouter>();
        //    var secretManager = new SecretManager(SettingsManager, repository, null);

        //    using (var manager = new WebScriptHostManager(config, new TestSecretManagerFactory(secretManager), eventManagerMock.Object, SettingsManager, webHostSettings, routerMock.Object))
        //    {
        //        Thread runLoopThread = new Thread(_ =>
        //        {
        //            manager.RunAndBlock(CancellationToken.None);
        //        });
        //        runLoopThread.IsBackground = true;
        //        runLoopThread.Start();

        //        await TestHelpers.Await(() =>
        //        {
        //            return manager.State == ScriptHostState.Running;
        //        });

        //        var request = new HttpRequestMessage(HttpMethod.Get, string.Format("http://localhost/api/httptrigger-{0}", fixture));
        //        FunctionDescriptor function = manager.GetHttpFunctionOrNull(request);

        //        SynchronizationContext currentContext = SynchronizationContext.Current;
        //        var resetEvent = new ManualResetEventSlim();

        //        try
        //        {
        //            var requestThread = new Thread(() =>
        //            {
        //                var context = new SingleThreadedSynchronizationContext();
        //                SynchronizationContext.SetSynchronizationContext(context);

        //                manager.HandleRequestAsync(function, request, CancellationToken.None)
        //                .ContinueWith(task => resetEvent.Set());

        //                Thread.Sleep(500);
        //                context.Run();
        //            });

        //            requestThread.IsBackground = true;
        //            requestThread.Start();

        //            bool threadSignaled = resetEvent.Wait(TimeSpan.FromSeconds(10));

        //            requestThread.Abort();

        //            Assert.True(threadSignaled, "Thread execution did not complete");
        //        }
        //        finally
        //        {
        //            SynchronizationContext.SetSynchronizationContext(currentContext);
        //            manager.Stop();
        //        }
        //    }
        //}
    }
}
