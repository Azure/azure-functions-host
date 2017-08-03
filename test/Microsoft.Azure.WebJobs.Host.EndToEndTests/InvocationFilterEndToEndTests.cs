// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.EndToEndTests
{
    public class InvocationFilterEndToEndTests
    {
        private readonly TestLoggerProvider _loggerProvider = new TestLoggerProvider();

        // This test will make sure the ordering of filters are correct. Each letter corresponds to a filter.
        // Executed filters have the parenthesis around them to denote an executed filter.
        // For example, a function with one executing and executed filter should have logs this in order: "A(A)"
        // For another example, a function with two executing and executed filters should log this: "AB(B)(A)"
        [Fact]
        public async Task TestFilterOrdering()
        {
            // Setup the test in memory
            var activator = new FakeActivator();
            var loggerFactory = new LoggerFactory();
            var logger = new MyLogger();
            activator.Add(new FilterOrderTestClass());
            activator.Add(new FilterOrderTestClass.SampleClassForFilterOrderTest());
            var typeLocator = new FakeTypeLocator(typeof(FilterOrderTestClass.SampleClassForFilterOrderTest), typeof(FilterOrderTestClass));

            var host = TestHelpers.NewJobHost<FilterOrderTestClass>(activator, logger, loggerFactory, typeLocator);
            loggerFactory.AddProvider(_loggerProvider);
            var method = typeof(FilterOrderTestClass).GetMethod("Main", BindingFlags.Public | BindingFlags.Instance);

            // Invoke the method
            await host.CallAsync(method);

            // Setup the logger information
            var loggerToTest = _loggerProvider.CreatedLoggers.Where(l => l.Category == LogCategories.Executor).Single();

            // Setup a string to make sure the filters were ran in the correct order
            string results = "";
            foreach (var message in loggerToTest.LogMessages)
            {
                // One is for the executing filter ("A"), three is for the executed filters ("(A)")
                if (message.FormattedMessage.Length == 1 || message.FormattedMessage.Length == 3 || message.FormattedMessage == "main")
                {
                    results += message.FormattedMessage;
                }
            }

            Assert.Equal("AB(B)(A)", results);
        }

        // This test will make sure the ordering of filters are correct if a filter fails. Each letter corresponds to a filter.
        // Executed filters have the parenthesis around them to denote an executed filter.
        // For example, a function with one executing and executed filter should have logs this in order: "A(A)"
        // For another example, a function with two executing and executed filters should log this: "AB(B)(A)"
        [Fact]
        public async Task TestOrderingWithFilterFails()
        {
            // Setup the test in memory
            var activator = new FakeActivator();
            var loggerFactory = new LoggerFactory();
            var logger = new MyLogger();
            activator.Add(new FailingFilterOrderTestClass());
            activator.Add(new FailingFilterOrderTestClass.SampleClassForFilterOrderTest());

            var typeLocator = new FakeTypeLocator(typeof(FailingFilterOrderTestClass.SampleClassForFilterOrderTest), typeof(FailingFilterOrderTestClass));
            var host = TestHelpers.NewJobHost<FailingFilterOrderTestClass>(activator, logger, loggerFactory, typeLocator);
            loggerFactory.AddProvider(_loggerProvider);
            var method = typeof(FailingFilterOrderTestClass).GetMethod("Main", BindingFlags.Public | BindingFlags.Instance);

            // Invoke the method
            try
            {
                await host.CallAsync(method);
            }
            catch
            {
            }

            // Setup the logger information
            var loggerToTest = _loggerProvider.CreatedLoggers.Where(l => l.Category == LogCategories.Executor).Single();

            // Setup a string to make sure the filters were ran in the correct order
            string results = "";
            foreach (var message in loggerToTest.LogMessages)
            {
                // One is for the executing filter ("A"), three is for the executed filters ("(A)")
                if (message.FormattedMessage.Length == 1 || message.FormattedMessage.Length == 3)
                {
                    results += message.FormattedMessage;
                }
            }

            Assert.Equal("AB(A)", results);
        }

        [Fact]
        public async Task TestPassingPropertiesInFilters()
        {
            // Setup the test in memory
            var activator = new FakeActivator();
            var loggerFactory = new LoggerFactory();
            var logger = new MyLogger();
            activator.Add(new StandardFilterTests());
            var host = TestHelpers.NewJobHost<StandardFilterTests>(activator, logger, loggerFactory);
            loggerFactory.AddProvider(_loggerProvider);

            // Invoke the method
            await host.CallAsync(nameof(StandardFilterTests.TestPropertiesInFunctionFilter));

            // Make sure the logs are correct
            var loggerToTest = _loggerProvider.CreatedLoggers.Where(l => l.Category == LogCategories.Executor).Single();
            Assert.NotNull(loggerToTest.LogMessages.SingleOrDefault(p => p.FormattedMessage.Contains("filters!")));
        }

        // This test is an example of a simple logging scenario
        [Fact]
        public async Task TestInvocationLoggingFilter()
        {
            // Setup the test in memory
            var activator = new FakeActivator();
            var loggerFactory = new LoggerFactory();
            var logger = new MyLogger();
            activator.Add(new StandardFilterTests());
            var host = TestHelpers.NewJobHost<StandardFilterTests>(activator, logger, loggerFactory);
            loggerFactory.AddProvider(_loggerProvider);

            // Invoke the method
            await host.CallAsync(nameof(StandardFilterTests.UseLoggingFilter));

            // Make sure the logs are correct
            var loggerToTest = _loggerProvider.CreatedLoggers.Where(l => l.Category == LogCategories.Executor).Single();
            Assert.NotNull(loggerToTest.LogMessages.SingleOrDefault(p => p.FormattedMessage.Contains("Test executing!")));
            Assert.NotNull(loggerToTest.LogMessages.SingleOrDefault(p => p.FormattedMessage.Contains("Test executed!")));
        }

        // This test reenacts a scenario where the filter utilizes the HTTPRequest for a function
        // A user can perform actions with the request, like authentication, before running the actual function.
        [Fact]
        public async Task TestHttpRequestFilter()
        {
            // Setup the test in memory
            var activator = new FakeActivator();
            var loggerFactory = new LoggerFactory();
            var logger = new MyLogger();
            activator.Add(new StandardFilterTests());
            var host = TestHelpers.NewJobHost<StandardFilterTests>(activator, logger, loggerFactory);
            loggerFactory.AddProvider(_loggerProvider);

            // Setup the HTTPRequest for the test
            HttpRequestMessage testHttpMessage = new HttpRequestMessage();

            // Invoke the method
            await host.CallAsync("UseHTTPRequestFilter", new { req = testHttpMessage });

            // Make sure the filter found the header
            var loggerToTest = _loggerProvider.CreatedLoggers.Where(l => l.Category == LogCategories.Executor).Single();
            Assert.NotNull(loggerToTest.LogMessages.SingleOrDefault(p => p.FormattedMessage.Contains("Found the header!")));
        }

        // This test checks the proper behavior and exception tracing of a failing Executing filter
        [Fact]
        public async Task TestFailingExecutingFilter()
        {
            // Setup the test in memory
            var activator = new FakeActivator();
            var loggerFactory = new LoggerFactory();
            var logger = new MyLogger();
            activator.Add(new StandardFilterTests());
            var config = TestHelpers.NewConfig<StandardFilterTests>(activator, logger, loggerFactory);

            loggerFactory.AddProvider(_loggerProvider);
            TestTraceWriter trace = new TestTraceWriter(TraceLevel.Verbose);
            config.Tracing.Tracers.Add(trace);

            var host = new JobHost(config);

            var method = typeof(StandardFilterTests).GetMethod("TestFailingExecutingFilter", BindingFlags.Public | BindingFlags.Instance);

            // Invoke the method
            try
            {
                await host.CallAsync(method);
            }
            catch
            {
            }

            string expectedName = $"{method.DeclaringType.FullName}.{method.Name}";

            // Make sure the tracing is correct
            TraceEvent[] traceErrors = trace.Traces.Where(p => p.Level == TraceLevel.Error).ToArray();
            Assert.Equal(3, traceErrors.Length);

            // Ensure that all errors include the same exception, with function
            // invocation details           
            FunctionInvocationException functionException = traceErrors.First().Exception as FunctionInvocationException;
            Assert.NotNull(functionException);
            Assert.NotEqual(Guid.Empty, functionException.InstanceId);
            Assert.Equal(expectedName, functionException.MethodName);
            Assert.True(traceErrors.All(p => functionException == p.Exception));

            // Validate Logger
            // Logger only writes out a single log message (which includes the Exception). 
            var loggerToTest = _loggerProvider.CreatedLoggers.Where(l => l.Category == LogCategories.Results).Single();
            var logMessage = loggerToTest.LogMessages.Single();
            var loggerException = logMessage.Exception as FunctionException;
            Assert.NotNull(loggerException);
            Assert.Equal(expectedName, loggerException.MethodName);
        }

        // This test checks the proper behavior and exception tracing of a failing Executed filter
        [Fact]
        public async Task TestFailingExecutedFilter()
        {
            // Setup the test in memory
            var activator = new FakeActivator();
            var loggerFactory = new LoggerFactory();
            var logger = new MyLogger();
            activator.Add(new StandardFilterTests());

            var config = TestHelpers.NewConfig<StandardFilterTests>(activator, logger, loggerFactory);
            loggerFactory.AddProvider(_loggerProvider);
            TestTraceWriter trace = new TestTraceWriter(TraceLevel.Verbose);
            config.Tracing.Tracers.Add(trace);

            var host = new JobHost(config);

            var method = typeof(StandardFilterTests).GetMethod("TestFailingExecutedFilter", BindingFlags.Public | BindingFlags.Instance);

            // Invoke the method
            try
            {
                await host.CallAsync(method);
            }
            catch
            {
            }

            string expectedName = $"{method.DeclaringType.FullName}.{method.Name}";

            // Make sure the tracing is correct
            TraceEvent[] traceErrors = trace.Traces.Where(p => p.Level == TraceLevel.Error).ToArray();
            Assert.Equal(3, traceErrors.Length);

            // Ensure that all errors include the same exception, with function
            // invocation details           
            FunctionInvocationException functionException = traceErrors.First().Exception as FunctionInvocationException;
            Assert.NotNull(functionException);
            Assert.NotEqual(Guid.Empty, functionException.InstanceId);
            Assert.Equal(expectedName, functionException.MethodName);
            Assert.True(traceErrors.All(p => functionException == p.Exception));

            // Validate Logger
            // Logger only writes out a single log message (which includes the Exception). 
            var loggerToTest = _loggerProvider.CreatedLoggers.Where(l => l.Category == LogCategories.Results).Single();
            var logMessage = loggerToTest.LogMessages.Single();
            var loggerException = logMessage.Exception as FunctionException;
            Assert.NotNull(loggerException);
            Assert.Equal(expectedName, loggerException.MethodName);
        }

        // This test checks the proper behavior and exception tracing of a failing function filter
        [Fact]
        public async Task TestFailingFunctionFilter()
        {
            // Setup the test in memory
            var activator = new FakeActivator();
            var loggerFactory = new LoggerFactory();
            var logger = new MyLogger();
            activator.Add(new StandardFilterTests());
            var config = TestHelpers.NewConfig<StandardFilterTests>(activator, logger, loggerFactory);
            loggerFactory.AddProvider(_loggerProvider);
            TestTraceWriter trace = new TestTraceWriter(TraceLevel.Verbose);
            config.Tracing.Tracers.Add(trace);

            var host = new JobHost(config);
            
            var method = typeof(StandardFilterTests).GetMethod("TestFailingFunctionFilter", BindingFlags.Public | BindingFlags.Instance);

            // Invoke the method
            try
            {
                await host.CallAsync(method);
            }
            catch
            {
            }

            string expectedName = $"{method.DeclaringType.FullName}.{method.Name}";

            // Verify that the executed filter didn't run
            var loggerToTest = _loggerProvider.CreatedLoggers.Where(l => l.Category == LogCategories.Executor).Single();
            Assert.Null(loggerToTest.LogMessages.SingleOrDefault(p => !p.FormattedMessage.Contains("The function failed!")));
            Assert.NotNull(loggerToTest.LogMessages.SingleOrDefault(p => !p.FormattedMessage.Contains("Test function invoked!")));

            // Make sure the tracing is correct
            TraceEvent[] traceErrors = trace.Traces.Where(p => p.Level == TraceLevel.Error).ToArray();
            Assert.Equal(3, traceErrors.Length);

            // Ensure that all errors include the same exception, with function
            // invocation details           
            FunctionInvocationException functionException = traceErrors.First().Exception as FunctionInvocationException;
            Assert.NotNull(functionException);
            Assert.NotEqual(Guid.Empty, functionException.InstanceId);
            Assert.Equal(expectedName, functionException.MethodName);
            Assert.True(traceErrors.All(p => functionException == p.Exception));

            // Validate Logger
            // Logger only writes out a single log message (which includes the Exception). 
            loggerToTest = _loggerProvider.CreatedLoggers.Where(l => l.Category == LogCategories.Results).Single();
            var logMessage = loggerToTest.LogMessages.Single();
            var loggerException = logMessage.Exception as FunctionException;
            Assert.NotNull(loggerException);
            Assert.Equal(expectedName, loggerException.MethodName);
        }

        public class MyLogger : IAsyncCollector<FunctionInstanceLogEntry>
        {
            public List<string> _items = new List<string>();

            public Task AddAsync(FunctionInstanceLogEntry item, CancellationToken cancellationToken = default(CancellationToken))
            {
                _items.Add(item.FunctionName);
                return Task.CompletedTask;
            }

            public Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken))
            {
                return Task.CompletedTask;
            }
        }
    }

    public class FilterOrderTestClass : IFunctionInvocationFilter
    {
        [NoAutomaticTrigger]
        [InvokeFunctionFilter(executingFilter: "FirstFunction", executedFilter: "SecondFunction")]
        public void Main(ILogger logger)
        {
        }

        public Task OnExecutingAsync(FunctionExecutingContext executingContext, CancellationToken cancellationToken)
        {
            executingContext.Logger.LogInformation("A");
            return Task.CompletedTask;
        }

        public Task OnExecutedAsync(FunctionExecutedContext executedContext, CancellationToken cancellationToken)
        {
            executedContext.Logger.LogInformation("(A)");
            return Task.CompletedTask;
        }

        public class SampleClassForFilterOrderTest
        {
            [NoAutomaticTrigger]
            public void FirstFunction(FunctionExecutingContext executingContext)
            {
                executingContext.Logger.LogInformation("B");
            }

            [NoAutomaticTrigger]
            public void SecondFunction(FunctionExecutedContext executedContext)
            {
                executedContext.Logger.LogInformation("(B)");
            }
        }
    }

    public class FailingFilterOrderTestClass : IFunctionInvocationFilter
    {
        [NoAutomaticTrigger]
        [InvokeFunctionFilter(executingFilter: "FirstFunction", executedFilter: "SecondFunction")]
        public void Main(ILogger logger)
        {
            logger.LogInformation("main");
        }

        public Task OnExecutingAsync(FunctionExecutingContext executingContext, CancellationToken cancellationToken)
        {
            executingContext.Logger.LogInformation("A");
            return Task.CompletedTask;
        }

        public Task OnExecutedAsync(FunctionExecutedContext executedContext, CancellationToken cancellationToken)
        {
            executedContext.Logger.LogInformation("(A)");
            return Task.CompletedTask;
        }

        public class SampleClassForFilterOrderTest
        {
            [NoAutomaticTrigger]
            public void FirstFunction(FunctionExecutingContext executingContext)
            {
                executingContext.Logger.LogInformation("B");
                throw new Exception("Throwing B on purpose");
            }

            [NoAutomaticTrigger]
            public void SecondFunction(FunctionExecutedContext executedContext)
            {
                executedContext.Logger.LogInformation("(B)");
            }
        }
    }

    public class StandardFilterTests
    {
        [NoAutomaticTrigger]
        [TestLoggingFilter]
        public void UseLoggingFilter(ILogger logger)
        {
            logger.LogInformation("Test function invoked!");
        }

        [NoAutomaticTrigger]
        [TestUserAuthorizationFilter(AllowedUsers = "Admin")]
        public void UseUserAuthorizationFilter(ILogger logger)
        {
            logger.LogInformation("Test function invoked!");
        }

        [NoAutomaticTrigger]
        [HTTPRequestFilter]
        public void UseHTTPRequestFilter(HttpRequestMessage req, ILogger logger)
        {
            logger.LogInformation("Test function invoked!");
        }

        [NoAutomaticTrigger]
        [TestUserAuthorizationFilter(AllowedUsers = "Dave")]
        public void TestFalseUserAuthorizationFilter(ILogger logger)
        {
            logger.LogInformation("Test function invoked!");
        }

        [NoAutomaticTrigger]
        [TestFailingFilter(true)]
        public void TestFailingExecutingFilter(ILogger logger)
        {
            logger.LogInformation("Test function invoked!");
        }

        [NoAutomaticTrigger]
        [TestFailingFilter]
        public void TestFailingExecutedFilter(ILogger logger)
        {
            logger.LogInformation("Test function invoked!");
        }

        [NoAutomaticTrigger]
        [TestFailingFunctionFilter]
        public void TestFailingFunctionFilter(ILogger logger)
        {
            logger.LogInformation("Test function invoked!");
            throw new Exception("Testing function fail");
        }

        [NoAutomaticTrigger]
        [InvokeFunctionFilter("MyFunction")]
        public void TestInvokeFunctionFilter(ILogger logger)
        {
            logger.LogInformation("Test function invoked!");
        }

        [NoAutomaticTrigger]
        [InvokeFunctionFilter(executingFilter: "PassProperty", executedFilter: "CatchProperty")]
        public void TestPropertiesInFunctionFilter(ILogger logger)
        {
        }

        [NoAutomaticTrigger]
        public void PassProperty(FunctionExecutingContext executingContext)
        {
            // Add the property to pass
            executingContext.Properties.Add("fakeproperty", "filters!");
            executingContext.Logger.LogInformation("Property was added to context");
        }

        [NoAutomaticTrigger]
        public void CatchProperty(FunctionExecutedContext executedContext)
        {
            // Read the passed property
            executedContext.Logger.LogInformation((string)executedContext.Properties["fakeproperty"]);
            executedContext.Logger.LogInformation("Property from context was logged");
        }
    }

    public class TestLoggingFilter : InvocationFilterAttribute
    {
        public override Task OnExecutingAsync(FunctionExecutingContext executingContext, CancellationToken cancellationToken)
        {
            executingContext.Logger.LogInformation("Test executing!");
            return Task.CompletedTask;
        }

        public override Task OnExecutedAsync(FunctionExecutedContext executedContext, CancellationToken cancellationToken)
        {
            executedContext.Logger.LogInformation("Test executed!");
            return Task.CompletedTask;
        }
    }

    public class TestUserAuthorizationFilter : InvocationFilterAttribute
    {
        public string AllowedUsers { get; set; }

        public override Task OnExecutingAsync(FunctionExecutingContext executingContext, CancellationToken cancellationToken)
        {
            executingContext.Logger.LogInformation("Test executing!");

            if (!AllowedUsers.Contains("Admin"))
            {
                executingContext.Logger.LogInformation("This is an unauthorized user!");
                throw new Exception("Not Allowing Unauthorized Users!");
            }

            executingContext.Logger.LogInformation("This is an authorized user!");
            return Task.CompletedTask;
        }
    }

    public class HTTPRequestFilter : InvocationFilterAttribute
    {
        public HttpRequestMessage HttpRequestToValidate { get; set; }

        public override Task OnExecutingAsync(FunctionExecutingContext executingContext, CancellationToken cancellationToken)
        {
            executingContext.Logger.LogInformation("Test executing!");

            IReadOnlyDictionary<string, object> arguments = executingContext.Arguments;
            var testValues = string.Join(",", arguments.Values.ToArray());

            if (testValues.Contains("Headers"))
            {
                executingContext.Logger.LogInformation("Found the header!");
            }

            return Task.CompletedTask;
        }
    }

    public class TestFailingFilter : InvocationFilterAttribute
    {
        bool failExecutingInvocation = false;

        public TestFailingFilter(bool input = false)
        {
            failExecutingInvocation = input;
        }

        public override Task OnExecutingAsync(FunctionExecutingContext executingContext, CancellationToken cancellationToken)
        {
            if (failExecutingInvocation == true)
            {
                executingContext.Logger.LogInformation("Failing executing invocation!");
                throw new Exception("Failing on purpose!");
            }

            return Task.CompletedTask;
        }

        public override Task OnExecutedAsync(FunctionExecutedContext executedContext, CancellationToken cancellationToken)
        {
            if (failExecutingInvocation == true)
            {
                return Task.CompletedTask;
            }

            executedContext.Logger.LogInformation("Failing executed invocation!");
            throw new Exception("Failing on purpose!");
        }
    }

    public class TestFailingFunctionFilter : InvocationFilterAttribute
    {
        public override Task OnExecutedAsync(FunctionExecutedContext executedContext, CancellationToken cancellationToken)
        {
            executedContext.Logger.LogInformation("The function failed!");
            executedContext.Logger.LogInformation(executedContext.FunctionResult.Exception.ToString());

            return Task.CompletedTask;
        }
    }
}