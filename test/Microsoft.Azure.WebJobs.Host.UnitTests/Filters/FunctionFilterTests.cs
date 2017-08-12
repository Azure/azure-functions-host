// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Executors
{
    public class FunctionFilterTests
    {
        public static StringBuilder TestLog = new StringBuilder();

        static string _throwAtPhase;
        static Exception _lastError;

        public FunctionFilterTests()
        {
            TestLog.Clear();
            _throwAtPhase = null;
            _lastError = null;
        }

        // A B C Body --> C' B' A'
        [Fact]
        public async Task SuccessTest()
        {
            var host = TestHelpers.NewJobHost<MyProg3>();

            await host.CallAsync("MyProg3.Method2");
            var expected =
                "[ctor][Pre-Instance][Pre_class][Pre_m1][Pre_m2]" +
                "[body]" +
                "[Post_m2][Post_m1][Post_class][Post-Instance]";

            Assert.Equal(expected, TestLog.ToString());
        }

        // A B C Body* --> C' B' A'        
        [Fact]
        public async Task FailInBody()
        {  
            var host = TestHelpers.NewJobHost<MyProg3>();

            // Fail in body. Post-filters still execute since their corresponding Pre executed.
            _throwAtPhase = "body";
                        
            await CallExpectFailureAsync(host);

            var expected =
                "[ctor][Pre-Instance][Pre_class][Pre_m1][Pre_m2]" +
                "[body-Throw!]" +
                "[Post_m2][Post_m1][Post_class][Post-Instance]" +
                "[ExceptionFilter_Instance][ExceptionFilter_class][ExceptionFilter_method]";

            Assert.Equal(expected, TestLog.ToString());
        }

        // A B* --> A'
        [Fact]
        public async Task FailInPreFilter()
        {
            var host = TestHelpers.NewJobHost<MyProg3>();

            // Fail in pre-filter. 
            // Subsequent pre-filters (and body) don't run.
            // But any post filter (who's pre-filter succeeded) should still run.
            _throwAtPhase = "Pre_m1";

            await CallExpectFailureAsync(host);

            var expected =
                "[ctor][Pre-Instance][Pre_class][Pre_m1-Throw!]" +
                "[Post_class][Post-Instance]" +
                "[ExceptionFilter_Instance][ExceptionFilter_class][ExceptionFilter_method]";

            Assert.Equal(expected, TestLog.ToString());
        }

        // A B C Body --> C' B'* A'
        [Fact]
        public async Task FailInPostFilter()
        {
            var host = TestHelpers.NewJobHost<MyProg3>();

            // Fail in post-filter! 
            // All post-filters should still run since their pre-filters ran. 
            _throwAtPhase = "Post_m1";

            await CallExpectFailureAsync(host);

            var expected =
                "[ctor][Pre-Instance][Pre_class][Pre_m1][Pre_m2]" +
                "[body]" +
                "[Post_m2][Post_m1-Throw!][Post_class][Post-Instance]" +
                "[ExceptionFilter_Instance][ExceptionFilter_class][ExceptionFilter_method]";

            Assert.Equal(expected, TestLog.ToString());
        }

        // A B C Body* --> C' B'* A'
        [Fact]
        public async Task DoubleFailure()
        {
            var loggerProvider = new TestLoggerProvider();

            // create a host with some global filters
            var host = TestHelpers.NewJobHost<MyProg3>(
                new MyInvocationFilterAttribute("global"), new MyExceptionFilterAttribute("global"), loggerProvider);

            _throwAtPhase = "body;Post_m1";

            await CallExpectFailureAsync(host);
            var expected =
                "[ctor][Pre-Instance][Pre_global][Pre_class][Pre_m1][Pre_m2]" +
                "[body-Throw!]" +
                "[Post_m2][Post_m1-Throw!][Post_class][Post_global][Post-Instance]" +
                "[ExceptionFilter_Instance][ExceptionFilter_global][ExceptionFilter_class][ExceptionFilter_method]";

            Assert.Equal(expected, TestLog.ToString());

            var logger = loggerProvider.CreatedLoggers.Single(p => p.Category == "Host.Executor");
            string logResult = string.Join("|", logger.LogMessages.Select(p => p.FormattedMessage));
            Assert.Equal(
                "Pre_global|Pre_class|Pre_m1|Pre_m2|" +
                "Post_m2|Post_m1|Post_class|Post_global|" +
                "ExceptionFilter_global|ExceptionFilter_class|ExceptionFilter_method", logResult);
        }

        [Fact]
        public async Task ActivationFailure()
        {
            var host = TestHelpers.NewJobHost<MyProg3>();

            _throwAtPhase = "ctor";

            // since instance creation fails, we don't expect the instance filter
            // to run
            await CallExpectFailureAsync(host);
            var expected = "[ctor-Throw!][ExceptionFilter_class][ExceptionFilter_method]";

            Assert.Equal(expected, TestLog.ToString());
        }

        [Fact]
        public async Task ExceptionFilterFailure()
        {
            // create a host with some global filters
            var host = TestHelpers.NewJobHost<MyProg3>(
                new MyInvocationFilterAttribute("global"), new MyExceptionFilterAttribute("global"));

            _throwAtPhase = "body;ExceptionFilter_global";

            await CallExpectFailureAsync(host);
            var expected =
                "[ctor][Pre-Instance][Pre_global][Pre_class][Pre_m1][Pre_m2]" +
                "[body-Throw!]" +
                "[Post_m2][Post_m1][Post_class][Post_global][Post-Instance]" +
                "[ExceptionFilter_Instance][ExceptionFilter_global-Throw!][ExceptionFilter_class][ExceptionFilter_method]";

            Assert.Equal(expected, TestLog.ToString());
        }

        // If a class implements IFunctionInvocationFilter, that filter is shared with the method instance. 
        [Fact]
        public async Task ClassFilterAndMethodShareInstance()
        {
            var host = TestHelpers.NewJobHost<MyProgInstanceFilter>();

            // Verify that:
            // - each instance calls [New] and [Dispose] 
            // - [Dispose] on the class comes after filters. 

            await host.CallAsync(nameof(MyProgInstanceFilter.Method));
            var fullPipeline = "[New][Pre-Instance][body][Post-Instance][Dispose]";
            Assert.Equal(fullPipeline, TestLog.ToString());

            // 2nd call invokes JobActivator again, which will new up a new instance 
            // So we should see another [New] tag in the log. 
            await host.CallAsync(nameof(MyProgInstanceFilter.Method));
            Assert.Equal(fullPipeline + fullPipeline, TestLog.ToString());
        }

        // Verify that there's a single instance of the attribute,
        // shared across all invocation instances 
        [Fact]
        public async Task SingleFilterInstanceOnClass()
        {
            MyInvocationFilterAttribute.Counter = 0;

            var host = TestHelpers.NewJobHost<MyProgWithClassFilter>();
                        
            await host.CallAsync(nameof(MyProgWithClassFilter.Method));
            Assert.Equal(1, MyInvocationFilterAttribute.Counter);

            await host.CallAsync(nameof(MyProgWithClassFilter.Method));
            await host.CallAsync(nameof(MyProgWithClassFilter.Method));

            Assert.Equal(1, MyInvocationFilterAttribute.Counter);
        }

        // Verify that there's a single instance of the attribute,
        // shared across all invocation instances 
        [Fact]
        public async Task SingleFilterInstanceOnMethod()
        {
            MyInvocationFilterAttribute.Counter = 0;

            var host = TestHelpers.NewJobHost<MyProgWithMethodFilter>();

            await host.CallAsync(nameof(MyProgWithMethodFilter.Method));
            Assert.Equal(1, MyInvocationFilterAttribute.Counter);

            await host.CallAsync("MyProgWithMethodFilter.Method");
            await host.CallAsync("MyProgWithMethodFilter.Method");

            Assert.Equal(1, MyInvocationFilterAttribute.Counter);
        }

        // Verify that all filters share the same instance of the property bag. 
        // Verify the filters can access the arguments. 
        [Fact]
        public void TestPropertyBag()
        {
            var host = TestHelpers.NewJobHost<MyProg6>();
            host.Call(nameof(MyProg6.Foo), new { myarg = MyProg6.ArgValue });

            Assert.Equal("[Pre-Instance][Pre-M1][Post-M1][Post-Instance]", MyProg6._sb.ToString());
        }

        public class MyProg6 : IFunctionInvocationFilter
        {
            const string Key = "k";
            public const string ArgValue = "x";

            public static StringBuilder _sb = new StringBuilder();

            public MyProg6()
            {
                _sb.Clear();
            }

            static void Append(FunctionInvocationContext context, string text)
            {
                var props = context.Properties;
                object obj;
                if (!props.TryGetValue(Key, out obj))
                {
                    obj = _sb;
                    props[Key] = obj;
                }
                var sb = (StringBuilder)obj;
                sb.Append(text);
            }

            [NoAutomaticTrigger]
            [MyFilter]
            public void Foo(string myarg)
            {
            }

            public Task OnExecutedAsync(FunctionExecutedContext executedContext, CancellationToken cancellationToken)
            {
                Append(executedContext, "[Post-Instance]");
                Assert.Equal(ArgValue, executedContext.Arguments["myarg"]);
                return Task.CompletedTask;
            }

            public Task OnExecutingAsync(FunctionExecutingContext executingContext, CancellationToken cancellationToken)
            {
                Append(executingContext, "[Pre-Instance]");
                Assert.Equal(ArgValue, executingContext.Arguments["myarg"]);
                return Task.CompletedTask;
            }

            class MyFilterAttribute : FunctionInvocationFilterAttribute
            {
                public override Task OnExecutedAsync(FunctionExecutedContext executedContext, CancellationToken cancellationToken)
                {
                    Append(executedContext, "[Post-M1]");
                    Assert.Equal(ArgValue, executedContext.Arguments["myarg"]);
                    return Task.CompletedTask;
                }

                public override Task OnExecutingAsync(FunctionExecutingContext executingContext, CancellationToken cancellationToken)
                {
                    Append(executingContext, "[Pre-M1]");
                    Assert.Equal(ArgValue, executingContext.Arguments["myarg"]);
                    return Task.CompletedTask;
                }
            }
        }

        static async Task CallExpectFailureAsync(JobHost host)
        {
            bool succeed = false;
            try
            {
                await host.CallAsync(nameof(MyProg3.Method2));
                succeed = true;
            }
            catch (Exception e)
            {
                // Verify exception message comes at _throwAtPhase
                // Last exception wins.
                var e2 = e.InnerException ?? e;
                var lastThrowPhase = _throwAtPhase.Split(';').Reverse().First();
                Assert.True(e2.Message.Contains(lastThrowPhase));
            }
            Assert.False(succeed); // Expected method to fail
        }

        static void Verify(FunctionExecutedContext context)
        {
            if (_lastError != null)
            {
                Assert.False(context.FunctionResult.Succeeded);
                Assert.Equal(_lastError, context.FunctionResult.Exception);
            }
            else
            {
                Assert.True(context.FunctionResult.Succeeded);
                Assert.Null(context.FunctionResult.Exception);
            }
        }

        static void Act(string phase)
        {
            TestLog.Append("[" + phase);
            if (_throwAtPhase != null)
            {
                if (_throwAtPhase.Contains(phase))
                {
                    TestLog.Append("-Throw!]");
                    _lastError = new Exception($"Throw at {phase}");
                    throw _lastError;
                }
            }
            TestLog.Append("]");
        }        

        public class MyProgWithMethodFilter
        {
            [MyInvocationFilter]
            [NoAutomaticTrigger]
            public void Method()
            {                
            }
        }

        [MyInvocationFilter]
        public class MyProgWithClassFilter
        {            
            [NoAutomaticTrigger]
            public void Method()
            {
            }
        }

        public class MyProgInstanceFilter : IFunctionInvocationFilter, IDisposable
        {
            public bool _field;

            public MyProgInstanceFilter()
            {
                Act("New");
            }

            

            [NoAutomaticTrigger]
            public void Method()
            {
                Assert.True(_field); // set in filter
                Act("body");
            }

            public Task OnExecutingAsync(FunctionExecutingContext executingContext, CancellationToken cancellationToken)
            {
                Act("Pre-Instance");
                Assert.False(_field); // Not yet set
                _field = true;

                return Task.CompletedTask;
            }

            public Task OnExecutedAsync(FunctionExecutedContext executedContext, CancellationToken cancellationToken)
            {
                Act("Post-Instance");

                Assert.True(_field);  // set from filter.
                return Task.CompletedTask;
            }

            public void Dispose()
            {
                Act("Dispose");
            }
        }

        // Add filters everywhere, test ordering 
        [MyInvocationFilter("class")]
        [MyExceptionFilter("class")]
        public class MyProg3 : IFunctionInvocationFilter, IFunctionExceptionFilter
        {
            public MyProg3()
            {
                Act("ctor");
            }

            [NoAutomaticTrigger]
            [MyInvocationFilter("m1")]
            [MyInvocationFilter("m2")]
            [MyExceptionFilter("method")]
            public void Method2(ILogger logger)
            {
                logger.LogInformation("body");
                Act("body");
            }

            public Task OnExceptionAsync(FunctionExceptionContext exceptionContext, CancellationToken cancellationToken)
            {
                Act("ExceptionFilter_Instance");
                return Task.CompletedTask;
            }

            public Task OnExecutedAsync(FunctionExecutedContext executedContext, CancellationToken cancellationToken)
            {
                Verify(executedContext);
                Act("Post-Instance");
                return Task.CompletedTask;
            }

            public Task OnExecutingAsync(FunctionExecutingContext executingContext, CancellationToken cancellationToken)
            {
                Act("Pre-Instance");
                return Task.CompletedTask;
            }
        }

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
        public class MyInvocationFilterAttribute : FunctionInvocationFilterAttribute
        {
            public static int Counter = 0;

            public string _id;

            public MyInvocationFilterAttribute(string id = null)
            {
                Counter++;
                _id = id;
            }

            public override Task OnExecutingAsync(FunctionExecutingContext executingContext, CancellationToken cancellationToken)
            {
                executingContext.Logger.LogInformation("Pre_" + _id);
                Act("Pre_" + _id);

                // add a custom property and retrieve it below
                executingContext.Properties["TestProperty"] = "TestValue";

                Assert.NotNull(executingContext.Logger);

                return base.OnExecutingAsync(executingContext, cancellationToken);
            }

            public override Task OnExecutedAsync(FunctionExecutedContext executedContext, CancellationToken cancellationToken)
            {
                Verify(executedContext);

                executedContext.Logger.LogInformation("Post_" + _id);
                Act("Post_" + _id);

                var value = executedContext.Properties["TestProperty"];
                Assert.Equal("TestValue", value);

                Assert.NotNull(executedContext.Logger);

                return base.OnExecutedAsync(executedContext, cancellationToken);
            }
        }

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
        public class MyExceptionFilterAttribute : FunctionExceptionFilterAttribute
        {
            public string _id;

            public MyExceptionFilterAttribute(string id = null)
            {
                _id = id;
            }

            public override Task OnExceptionAsync(FunctionExceptionContext exceptionContext, CancellationToken cancellationToken)
            {
                exceptionContext.Logger.LogInformation("ExceptionFilter_" + _id);
                Act("ExceptionFilter_" + _id);

                if (_throwAtPhase != "ctor")
                {
                    // verify that the property added by the invocation filter
                    // is available to all filters
                    var value = exceptionContext.Properties["TestProperty"];
                    Assert.Equal("TestValue", value);
                }

                Assert.NotNull(exceptionContext.Exception);
                Assert.NotNull(exceptionContext.ExceptionDispatchInfo);
                Assert.NotNull(exceptionContext.Logger);

                return Task.CompletedTask;
            }
        }
    }    
}