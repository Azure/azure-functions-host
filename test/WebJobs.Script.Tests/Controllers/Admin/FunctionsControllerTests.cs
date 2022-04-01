// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Management.Models;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionsControllerTests : IDisposable
    {
        private readonly TempDirectory _secretsDirectory = new TempDirectory();

        [Fact]
        public async Task Invoke_CallsFunction()
        {
            var testFunctions = new Collection<FunctionDescriptor>();
            string testFunctionName = "TestFunction";
            string triggerParameterName = "testTrigger";
            string testInput = Guid.NewGuid().ToString();
            bool functionInvoked = false;

            var scriptHostMock = new Mock<IScriptJobHost>();
            scriptHostMock.Setup(p => p.CallAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>(), CancellationToken.None))
            .Callback<string, IDictionary<string, object>, CancellationToken>((name, args, token) =>
            {
                // verify the correct arguments were passed to the invoke
                Assert.Equal(testFunctionName, name);
                Assert.Equal(1, args.Count);
                Assert.Equal(testInput, (string)args[triggerParameterName]);

                functionInvoked = true;
            })
            .Returns(Task.CompletedTask);

            scriptHostMock.Setup(p => p.Functions).Returns(testFunctions);

            // Add a few parameters, with the trigger parameter last
            // to verify parameter order handling
            Collection<ParameterDescriptor> parameters = new Collection<ParameterDescriptor>
            {
                new ParameterDescriptor("context", typeof(ExecutionContext)),
                new ParameterDescriptor("log", typeof(TraceWriter)),
                new ParameterDescriptor(triggerParameterName, typeof(string))
                {
                    IsTrigger = true
                }
            };
            testFunctions.Add(new FunctionDescriptor(testFunctionName, null, null, parameters, null, null, null));

            FunctionInvocation invocation = new FunctionInvocation
            {
                Input = testInput
            };

            var scriptPath = Path.GetTempPath();
            var applicationHostOptions = new ScriptApplicationHostOptions();
            applicationHostOptions.ScriptPath = scriptPath;
            var optionsWrapper = new OptionsWrapper<ScriptApplicationHostOptions>(applicationHostOptions);
            var functionsManagerMock = new Mock<IWebFunctionsManager>();
            var mockRouter = new Mock<IWebJobsRouter>();
            var testController = new FunctionsController(functionsManagerMock.Object, mockRouter.Object, new LoggerFactory(), optionsWrapper);
            IActionResult response = testController.Invoke(testFunctionName, invocation, scriptHostMock.Object);
            Assert.IsType<AcceptedResult>(response);

            // The call is fire-and-forget, so watch for functionInvoked to be set.
            await TestHelpers.Await(() => functionInvoked, timeout: 3000, pollingInterval: 100);

            Assert.True(functionInvoked);
        }

        [Fact]
        public async Task Invoke_CallsFunctionSession()
        {
            var testFunctions = new Collection<FunctionDescriptor>();
            string testFunctionName = "TestFunction";
            string triggerParameterName = "testTrigger";
            string testInput = Guid.NewGuid().ToString();
            string sessionId = Guid.NewGuid().ToString();
            bool baggageAdded = false;

            var scriptHostMock = new Mock<IScriptJobHost>();
            scriptHostMock.Setup(p => p.CallAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>(), CancellationToken.None))
            .Callback<string, IDictionary<string, object>, CancellationToken>((name, args, token) =>
            {
                if (string.Equals(Activity.Current?.GetBaggageItem(ScriptConstants.LiveLogsSessionAIKey), sessionId, StringComparison.OrdinalIgnoreCase))
                {
                    baggageAdded = true;
                }
            })
            .Returns(Task.CompletedTask);

            scriptHostMock.Setup(p => p.Functions).Returns(testFunctions);
            Collection<ParameterDescriptor> parameters = new Collection<ParameterDescriptor>
            {
                new ParameterDescriptor("context", typeof(ExecutionContext)),
                new ParameterDescriptor("log", typeof(TraceWriter)),
                new ParameterDescriptor(triggerParameterName, typeof(string))
                {
                    IsTrigger = true
                }
            };
            testFunctions.Add(new FunctionDescriptor(testFunctionName, null, null, parameters, null, null, null));

            FunctionInvocation invocation = new FunctionInvocation
            {
                Input = testInput
            };

            var scriptPath = Path.GetTempPath();
            var applicationHostOptions = new ScriptApplicationHostOptions();
            applicationHostOptions.ScriptPath = scriptPath;
            var optionsWrapper = new OptionsWrapper<ScriptApplicationHostOptions>(applicationHostOptions);
            var functionsManagerMock = new Mock<IWebFunctionsManager>();
            var mockRouter = new Mock<IWebJobsRouter>();
            var testController = new FunctionsController(functionsManagerMock.Object, mockRouter.Object, new LoggerFactory(), optionsWrapper);
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers.Add(ScriptConstants.LiveLogsSessionAIKey, sessionId);
            testController.ControllerContext.HttpContext = httpContext;

            //Test Code
            IActionResult response = testController.Invoke(testFunctionName, invocation, scriptHostMock.Object);
            Assert.IsType<AcceptedResult>(response);

            // The call is fire-and-forget, so watch for functionInvoked to be set.
            await TestHelpers.Await(() => baggageAdded, timeout: 3000, pollingInterval: 100);

            Assert.True(baggageAdded);
        }

        private static FunctionsController SetUpFunctionsController(string testFunctionName, bool isFileSystemReadOnly, bool functionCreatedSuccess = true)
        {
            var scriptPath = Path.GetTempPath();
            var applicationHostOptions = new ScriptApplicationHostOptions
            {
                ScriptPath = scriptPath,
                IsFileSystemReadOnly = isFileSystemReadOnly
            };

            var functionsMetadataMock = new Mock<FunctionMetadataResponse>();
            var functionsManagerMock = new Mock<IWebFunctionsManager>();
            functionsManagerMock.Setup(p => p.CreateOrUpdate(It.IsAny<string>(), It.IsAny<FunctionMetadataResponse>(), It.IsAny<HttpRequest>()))
            .Callback<string, FunctionMetadataResponse, HttpRequest>((name, functionMetadata, request) =>
            {
                // verify the correct arguments were passed to the method
                Assert.Equal(testFunctionName, name);
                Assert.Equal(request.Method, "Put");
            })
            .Returns(Task.FromResult((functionCreatedSuccess, true, functionsMetadataMock.Object)));

            var mockRouter = new Mock<IWebJobsRouter>();
            var optionsWrapper = new OptionsWrapper<ScriptApplicationHostOptions>(applicationHostOptions);
            var testController = new FunctionsController(functionsManagerMock.Object, mockRouter.Object, new LoggerFactory(), optionsWrapper);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Host = new HostString("local");
            httpContext.Request.Method = "Put";
            httpContext.Request.Path = $"/admin/functions/{testFunctionName}";
            testController.ControllerContext.HttpContext = httpContext;

            return testController;
        }

        [Fact]
        public async Task CreateOrUpdate_CreatesFunction()
        {
            var testFunctionName = "TestFunction";
            var fileSystemReadOnly = false;
            var testController = SetUpFunctionsController(testFunctionName, fileSystemReadOnly);

            var functionsMetadataMock = new Mock<FunctionMetadataResponse>();
            var fileMonitoringServiceMock = new Mock<IFileMonitoringService>();
            var result = (CreatedResult)await testController.CreateOrUpdate(testFunctionName, functionsMetadataMock.Object, fileMonitoringServiceMock.Object);

            Assert.Equal((int)HttpStatusCode.Created, result.StatusCode);
            Assert.True(result.Location.Contains($"/admin/functions/{testFunctionName}"));
        }

        [Fact]
        public async Task CreateOrUpdate_Fails()
        {
            var functionsMetadataMock = new Mock<FunctionMetadataResponse>();
            var fileMonitoringServiceMock = new Mock<IFileMonitoringService>();

            // issuing a PUT on a read-only filesystem results in 400
            var testFunctionName = "TestFunction";
            var testController = SetUpFunctionsController(testFunctionName, isFileSystemReadOnly: true);
            var result = (BadRequestObjectResult)await testController.CreateOrUpdate(testFunctionName, functionsMetadataMock.Object, fileMonitoringServiceMock.Object);

            Assert.Equal((int)HttpStatusCode.BadRequest, result.StatusCode);
            Assert.Equal(result.Value, "Your app is currently in read only mode. Cannot create or update functions.");

            // invalid function name results in 400
            testFunctionName = string.Empty;
            testController = SetUpFunctionsController(testFunctionName, isFileSystemReadOnly: false);
            result = (BadRequestObjectResult)await testController.CreateOrUpdate(testFunctionName, functionsMetadataMock.Object, fileMonitoringServiceMock.Object);

            Assert.Equal((int)HttpStatusCode.BadRequest, result.StatusCode);
            Assert.Equal(result.Value, $"{testFunctionName} is not a valid function name");

            // if function creation fails, we should respond with 500
            testFunctionName = "TestFunction";
            testController = SetUpFunctionsController(testFunctionName, isFileSystemReadOnly: false, functionCreatedSuccess: false);
            var response = (StatusCodeResult)await testController.CreateOrUpdate(testFunctionName, functionsMetadataMock.Object, fileMonitoringServiceMock.Object);

            Assert.Equal((int)HttpStatusCode.InternalServerError, response.StatusCode);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _secretsDirectory.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}