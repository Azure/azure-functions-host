// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.WindowsAzure.Storage;
using WebJobs.Script.WebHost;
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

        [Fact]
        public void Generate_WithValueTypes_Succeeds()
        {
            string functionName = "FunctionWithValueTypes";
            Collection<ParameterDescriptor> parameters = new Collection<ParameterDescriptor>();

            parameters.Add(new ParameterDescriptor("param1", typeof(string)));
            parameters.Add(new ParameterDescriptor("param2", typeof(DateTimeOffset)));
            parameters.Add(new ParameterDescriptor("param3", typeof(int)));

            FunctionMetadata metadata = new FunctionMetadata();
            object[] invocationArguments = null;
            TestInvoker invoker = new TestInvoker(args => { invocationArguments = args; });
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

            // Verify that the method is invocable
            DateTimeOffset input = DateTimeOffset.Now;
            method.Invoke(null, new object[] { "test", input, 44 });

            // verify our custom invoker was called
            Assert.Equal(1, invoker.InvokeCount);

            Assert.NotNull(invocationArguments);
            Assert.Equal(input, (DateTimeOffset)invocationArguments[1]);
            Assert.Equal(44, (int)invocationArguments[2]);
        }

        [Fact]
        public void Generate_WithOutParams_CorrectlyUpdatesOutput()
        {
            string functionName = "FunctionWithOutValue";
            Collection<ParameterDescriptor> parameters = new Collection<ParameterDescriptor>();

            parameters.Add(new ParameterDescriptor("param1", typeof(string).MakeByRefType()) { Attributes = ParameterAttributes.Out });

            FunctionMetadata metadata = new FunctionMetadata();
            object[] invocationArguments = null;
              TestInvoker invoker = new TestInvoker(args =>
            {
                invocationArguments = args;
                invocationArguments[0] = "newvalue";
            });
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

            // Verify that the method is invocable
            DateTimeOffset input = DateTimeOffset.Now;
            method.Invoke(null, new object[] { null });

            // verify our custom invoker was called
            Assert.Equal(1, invoker.InvokeCount);

            Assert.NotNull(invocationArguments);
            Assert.Equal("newvalue", (string)invocationArguments[0]);
        }

        [Fact]
        public void GeneratedMethods_WithOutParams_DoNotCauseDeadlocks()
        {
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);

            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = @"TestScripts\FunctionGeneration",
                TraceWriter = traceWriter
            };

            using (var manager = new WebScriptHostManager(config))
            {
                Thread runLoopThread = new Thread(_ =>
                {
                    manager.RunAndBlock(CancellationToken.None);
                });
                runLoopThread.IsBackground = true;
                runLoopThread.Start();
                
                while (!manager.IsRunning)
                {
                    Thread.Sleep(100);
                }

                FunctionDescriptor function = manager.GetHttpFunctionOrNull(new Uri("http://localhost/api/httptrigger-csharp"));
                var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/httptrigger-csharp");

                SynchronizationContext currentContext = SynchronizationContext.Current;
                var resetEvent = new ManualResetEventSlim();

                try
                {
                    var requestThread = new Thread(() =>
                    {
                        var context = new SingleThreadSynchronizationContext();
                        SynchronizationContext.SetSynchronizationContext(context);

                        manager.HandleRequestAsync(function, request, CancellationToken.None)
                        .ContinueWith(task => resetEvent.Set());

                        Thread.Sleep(500);
                        context.Run();
                    });

                    requestThread.IsBackground = true;
                    requestThread.Start();

                    bool threadSignaled = resetEvent.Wait(TimeSpan.FromSeconds(10));

                    requestThread.Abort();

                    Assert.True(threadSignaled, "Thread execution did not complete");
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(currentContext);
                    manager.Stop();
                }
            }
        }

        private sealed class SingleThreadSynchronizationContext : SynchronizationContext
        {
            private readonly ConcurrentQueue<Tuple<SendOrPostCallback, object>> _workItems =
                new ConcurrentQueue<Tuple<SendOrPostCallback, object>>();
          
            public override void Post(SendOrPostCallback d, object state)
            {
                _workItems.Enqueue(new Tuple<SendOrPostCallback, object>(d, state));
            }

            public override void Send(SendOrPostCallback d, object state)
            {
                throw new NotSupportedException();
            }

            public void Run()
            {
                Tuple<SendOrPostCallback, object> item;
                while (_workItems.TryDequeue(out item))
                {
                    item.Item1(item.Item2);
                }
            }
        }
    }
}
