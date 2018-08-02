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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class AdminControllerTests : IDisposable
    {
        private readonly ScriptSettingsManager _settingsManager;
        private readonly TempDirectory _secretsDirectory = new TempDirectory();
        private readonly Mock<ISecretManager> _secretsManagerMock;
        private Mock<IScriptJobHost> _hostMock;
        private Collection<FunctionDescriptor> _testFunctions;
        private FunctionsController _testController;

        public AdminControllerTests()
        {
            _settingsManager = ScriptSettingsManager.Instance;
            _testFunctions = new Collection<FunctionDescriptor>();

            var config = new ScriptHostOptions();
            var environment = new NullScriptHostEnvironment();
            var eventManager = new Mock<IScriptEventManager>();
            var mockRouter = new Mock<IWebJobsRouter>();
            var mockWebFunctionManager = new Mock<IWebFunctionsManager>();
            _hostMock = new Mock<IScriptJobHost>(MockBehavior.Strict, new object[] { environment, eventManager.Object, config, null, null, null });
            _hostMock.Setup(p => p.Functions).Returns(_testFunctions);

            var settings = new ScriptWebHostOptions();
            settings.SecretsPath = _secretsDirectory.Path;
            _secretsManagerMock = new Mock<ISecretManager>(MockBehavior.Strict);

            testController = new FunctionsController(mockWebFunctionManager.Object, managerMock.Object, mockRouter.Object, new LoggerFactory());
        }

        [Fact]
        public async Task Invoke_CallsFunction()
        {
            string testFunctionName = "TestFunction";
            string triggerParameterName = "testTrigger";
            string testInput = Guid.NewGuid().ToString();
            bool functionInvoked = false;

            _hostMock.Setup(p => p.CallAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), CancellationToken.None))
                .Callback<string, Dictionary<string, object>, CancellationToken>((name, args, token) =>
                {
                    // verify the correct arguments were passed to the invoke
                    Assert.Equal(testFunctionName, name);
                    Assert.Equal(1, args.Count);
                    Assert.Equal(testInput, (string)args[triggerParameterName]);

                    functionInvoked = true;
                })
                .Returns(Task.CompletedTask);

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
            _testFunctions.Add(new FunctionDescriptor(testFunctionName, null, null, parameters, null, null, null));

            FunctionInvocation invocation = new FunctionInvocation
            {
                Input = testInput
            };
            IActionResult response = _testController.Invoke(testFunctionName, invocation, _hostMock.Object);
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
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
    }
}