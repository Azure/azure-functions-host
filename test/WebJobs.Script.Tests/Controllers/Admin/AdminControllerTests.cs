﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class AdminControllerTests : IDisposable
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

            var functionsManagerMock = new Mock<IWebFunctionsManager>();
            var mockRouter = new Mock<IWebJobsRouter>();
            var testController = new FunctionsController(functionsManagerMock.Object, mockRouter.Object, new LoggerFactory());
            IActionResult response = testController.Invoke(testFunctionName, invocation, scriptHostMock.Object);
            Assert.IsType<AcceptedResult>(response);

            // The call is fire-and-forget, so watch for functionInvoked to be set.
            await TestHelpers.Await(() => functionInvoked, timeout: 3000, pollingInterval: 100);

            Assert.True(functionInvoked);
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