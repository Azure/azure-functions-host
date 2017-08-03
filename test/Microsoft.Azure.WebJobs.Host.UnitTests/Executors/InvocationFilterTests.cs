// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Executors
{
    public class InvocationFilterTests
    {
        public static StringBuilder _log = new StringBuilder();

        static string _throwAtPhase;

        public InvocationFilterTests()
        {
            _log.Clear();
            _throwAtPhase = null;
        }

        [Fact]
        // A B C Body --> C' B' A'
        public async Task SuccessTest()
        {
            var host = TestHelpers.NewJobHost<MyProg3>();

            // Ordering is Instace, Class, Method

            await host.CallAsync("MyProg3.Method2");
            var expected = 
                "[Pre-Instance][Pre_class][Pre_m1][Pre_m2]" +
                "[body]" +
                "[Post_m2][Post_m1][Post_class][Post-Instance]"; // Reverse order 

            Assert.Equal(expected, _log.ToString());
        }

        // A B C Body* --> C' B' A'        
        [Fact]
        public async Task FailInBody()
        {  
            var host = TestHelpers.NewJobHost<MyProg3>();

            // FAil in body. Post-filters still execute since their corresponding Pre excuted.
            _throwAtPhase = "body";
                        
            await CallExpectFailureAsync(host);

            var expected =
                "[Pre-Instance][Pre_class][Pre_m1][Pre_m2]" +
                "[body-Throw!]" +
                "[Post_m2][Post_m1][Post_class][Post-Instance]";

            Assert.Equal(expected, _log.ToString());
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
                "[Pre-Instance][Pre_class][Pre_m1-Throw!]" +
                "[Post_class][Post-Instance]"; // Reverse order 

            Assert.Equal(expected, _log.ToString());
        }

        // A B C Body --> C' B'* A'
        [Fact]
        public async Task FailInPostFilter()
        {
            var host = TestHelpers.NewJobHost<MyProg3>();

            // Fail in post-filter! 
            // All post-filters should still run since their pre-filters rn. 
            _throwAtPhase = "Post_m1";

            await CallExpectFailureAsync(host);

            var expected =
                "[Pre-Instance][Pre_class][Pre_m1][Pre_m2]" +
                "[body]" +
                "[Post_m2][Post_m1-Throw!][Post_class][Post-Instance]"; // Reverse order 

            Assert.Equal(expected, _log.ToString());
        }

        // A B C Body* --> C' B'* A'
        [Fact]
        public async Task DoubleFailure()
        {
            var host = TestHelpers.NewJobHost<MyProg3>();

            // Ordering is Instace, Class, Method
            _throwAtPhase = "body;Post_m1";

            await CallExpectFailureAsync(host);
            var expected =
                "[Pre-Instance][Pre_class][Pre_m1][Pre_m2]" +
                "[body-Throw!]" +
                "[Post_m2][Post_m1-Throw!][Post_class][Post-Instance]"; // Reverse order 

            Assert.Equal(expected, _log.ToString());
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
            Assert.Equal(fullPipeline, _log.ToString());

            // 2nd call invokes JobActivator again, which will new up a new instance 
            // So we should see another [New] tag in the log. 
            await host.CallAsync(nameof(MyProgInstanceFilter.Method));
            Assert.Equal(fullPipeline + fullPipeline, _log.ToString());
        }

        [Fact]
        // There's a single instance of the attribute, shared across many instances 
        public async Task SingleFilterInstanceOnClass()
        {
            MyFilterAttribute.Counter = 0;

            var host = TestHelpers.NewJobHost<MyProgWithClassFilter>();
                        
            await host.CallAsync(nameof(MyProgWithClassFilter.Method));
            Assert.Equal(1, MyFilterAttribute.Counter);

            await host.CallAsync(nameof(MyProgWithClassFilter.Method));
            await host.CallAsync(nameof(MyProgWithClassFilter.Method));

            Assert.Equal(1, MyFilterAttribute.Counter);
        }

        [Fact]
        // There's a single instance of the attribute, shared across many instances 
        public async Task SingleFilterInstanceOnMethod()
        {
            MyFilterAttribute.Counter = 0;

            var host = TestHelpers.NewJobHost<MyProgWithMethodFilter>();

            await host.CallAsync(nameof(MyProgWithMethodFilter.Method));
            Assert.Equal(1, MyFilterAttribute.Counter);

            await host.CallAsync("MyProgWithMethodFilter.Method");
            await host.CallAsync("MyProgWithMethodFilter.Method");

            Assert.Equal(1, MyFilterAttribute.Counter);
        }

        [Fact]
        public async Task InvokeMethods()
        {
            var host = TestHelpers.NewJobHost<MyProg5>();
            host.Call("Main");

            var fullPipeline = "[Pre][body][Post]";
            Assert.Equal(fullPipeline, _log.ToString());
        }

        // We should still be able to directly invoke a filter (such as for unit testing)
        [Fact]
        public async Task ExplicitInvokeFilter()
        {
            var host = TestHelpers.NewJobHost<MyProg5>();

            var filter = new FunctionExecutingContext()
            {
                FunctionName = "foo"
            };
            IDictionary<string, object> args = new Dictionary<string, object>
            {
                { "filter", filter } // match on type, not name 
            };
            await host.CallAsync("Pre", args, CancellationToken.None);
                        
            Assert.Equal("[Pre]", _log.ToString());
        }

        public class MyProg5
        {
            [FunctionName("Pre")]
            [NoAutomaticTrigger]
            public void Method1(FunctionExecutingContext pre)
            {
                Act("Pre");
            }

            [NoAutomaticTrigger]
            public void Post(FunctionExecutedContext ctx)
            {
                Act("Post");
            }

            [InvokeFunctionFilter("Pre", "Post")]
            [NoAutomaticTrigger]
            public void Main()
            {
                Act("body");
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
            catch (FunctionInvocationException e)
            {
                var e2 = e.InnerException;
                // Verify exception message comes _throwAtPhase
                // Last exception wins. 
                var lastThrowPhase = _throwAtPhase.Split(';').Reverse().First();
                Assert.True(e2.Message.Contains(lastThrowPhase));
            }
            Assert.False(succeed); // Expected ,method to fail
        }

        static void Act(string phase)
        {
            _log.Append("[" + phase);
            if (_throwAtPhase != null)
            {
                if (_throwAtPhase.Contains(phase))
                {
                    _log.Append("-Throw!]");
                    throw new Exception($"Throw at {phase}");
                }
            }
            _log.Append("]");
        }        

        // Basic program with methdo filter. 
        public class MyProgWithMethodFilter
        {
            [MyFilter]
            [NoAutomaticTrigger]
            public void Method()
            {                
            }
        }

        // Basic program with methdo filter. 
        [MyFilter]
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
        [MyFilter("class")]
        public class MyProg3 : IFunctionInvocationFilter
        {
            [NoAutomaticTrigger]
            [MyFilter("m1")]
            [MyFilter("m2")]
            public void Method2()
            {
                Act("body");
            }

            public Task OnExecutedAsync(FunctionExecutedContext executedContext, CancellationToken cancellationToken)
            {
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
        public class MyFilterAttribute : InvocationFilterAttribute
        {
            public static int Counter = 0;

            public string _id;

            public MyFilterAttribute(string id = null)
            {
                Counter++;
                _id = id;
            }

            public override Task OnExecutingAsync(FunctionExecutingContext executingContext, CancellationToken cancellationToken)
            {
                Act("Pre_" + _id);
                return base.OnExecutingAsync(executingContext, cancellationToken);
            }

            public override Task OnExecutedAsync(FunctionExecutedContext executedContext, CancellationToken cancellationToken)
            {
                Act("Post_" + _id);
                return base.OnExecutedAsync(executedContext, cancellationToken);
            }
        }
    }    
}