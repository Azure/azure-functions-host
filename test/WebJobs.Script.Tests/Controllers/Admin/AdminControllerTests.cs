// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class AdminControllerTests
    {
        private Mock<ScriptHost> hostMock;
        private Mock<WebScriptHostManager> managerMock;
        private Collection<FunctionDescriptor> testFunctions;
        private AdminController testController;

        public AdminControllerTests()
        {
            testFunctions = new Collection<FunctionDescriptor>();

            var config = new ScriptHostConfiguration();
            hostMock = new Mock<ScriptHost>(MockBehavior.Strict, new object[] { config });
            hostMock.Setup(p => p.Functions).Returns(testFunctions);

            ISecretManager secretManager = new SecretManager();
            WebHostSettings settings = new WebHostSettings();
            managerMock = new Mock<WebScriptHostManager>(MockBehavior.Strict, new object[] { config, secretManager, settings });
            managerMock.SetupGet(p => p.Instance).Returns(hostMock.Object);

            testController = new AdminController(managerMock.Object);
        }

        [Fact]
        public void HasAuthorizationLevelAttribute()
        {
            AuthorizationLevelAttribute attribute = typeof(AdminController).GetCustomAttribute<AuthorizationLevelAttribute>();
            Assert.Equal(AuthorizationLevel.Admin, attribute.Level);
        }

        [Fact]
        public async Task Invoke_CallsFunction()
        {
            string testFunctionName = "TestFunction";
            string triggerParameterName = "testTrigger";
            string testInput = Guid.NewGuid().ToString();
            bool functionInvoked = false;
            
            hostMock.Setup(p => p.CallAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), CancellationToken.None))
                .Callback<string, Dictionary<string, object>, CancellationToken>((name, args, token) =>
                {
                    functionInvoked = true;

                    // verify the correct arguments were passed to the invoke
                    Assert.Equal(testFunctionName, name);
                    Assert.Equal(1, args.Count);
                    Assert.Equal(testInput, (string)args[triggerParameterName]);
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
            testFunctions.Add(new FunctionDescriptor(testFunctionName, null, null, parameters));

            FunctionInvocation invocation = new FunctionInvocation
            {
                Input = testInput
            };
            HttpResponseMessage response = testController.Invoke(testFunctionName, invocation);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            // allow the invoke task to run
            await Task.Delay(200);

            Assert.True(functionInvoked);
        }
    }
}
