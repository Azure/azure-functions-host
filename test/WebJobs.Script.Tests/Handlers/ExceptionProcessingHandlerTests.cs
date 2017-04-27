// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Dependencies;
using System.Web.Http.ExceptionHandling;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Moq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ExceptionProcessingHandlerTests
    {
        private readonly ScriptSettingsManager _settingsManager;
        private readonly TempDirectory _secretsDirectory = new TempDirectory();
        private readonly System.Web.Http.HttpConfiguration _config;
        private readonly TestTraceWriter _traceWriter;

        public ExceptionProcessingHandlerTests()
        {
            _settingsManager = ScriptSettingsManager.Instance;
            _traceWriter = new TestTraceWriter(System.Diagnostics.TraceLevel.Verbose);

            Mock<IDependencyResolver> mockResolver = new Mock<IDependencyResolver>();
            mockResolver.Setup(p => p.GetService(typeof(TraceWriter))).Returns(_traceWriter);

            _config = new System.Web.Http.HttpConfiguration();
            _config.DependencyResolver = mockResolver.Object;
        }

        [Fact]
        public async Task Handle_WithAnonymousLevelAndNoErrorDetailsFlag_SetsResponseWithNoErrorDetailsAndGenericMessage()
        {
            var errorModel = await ExecuteHandlerTest(AuthorizationLevel.Anonymous, false);

            Assert.Null(errorModel.ErrorDetails);
            Assert.Equal($"An error has occurred. For more information, please check the logs for error ID {errorModel.Id}", errorModel.Message);
        }

        [Fact]
        public async Task Handle_WithAdminlAndNoErrorDetailsFlag_SetsResponseWithNoErrorDetailsAndExceptionMessage()
        {
            var errorModel = await ExecuteHandlerTest(AuthorizationLevel.Admin, false);

            Assert.Null(errorModel.ErrorDetails);
            Assert.Equal("TestException", errorModel.Message);
        }

        [Fact]
        public async Task Handle_WithAdminlAndErrorDetailsFlag_SetsResponseWithErrorDetailsAndExceptionMessage()
        {
            var errorModel = await ExecuteHandlerTest(AuthorizationLevel.Admin, true);

            Assert.Equal("System.Exception : TestException", errorModel.ErrorDetails);
            Assert.Equal("TestException", errorModel.Message);
        }

        [Fact]
        public async Task Handle_WithAnonymousAndErrorDetailsFlag_SetsResponseWithNoErrorDetailsAndExceptionMessage()
        {
            var errorModel = await ExecuteHandlerTest(AuthorizationLevel.Anonymous, true);

            Assert.Null(errorModel.ErrorDetails);
            Assert.Equal($"An error has occurred. For more information, please check the logs for error ID {errorModel.Id}", errorModel.Message);
        }

        private async Task<ApiErrorModel> ExecuteHandlerTest(AuthorizationLevel authLevel, bool includeDetails)
        {
            var handler = new ExceptionProcessingHandler(_config);
            var requestId = Guid.NewGuid().ToString();
            var exception = new Exception("TestException");
            var exceptionContext = new ExceptionContext(exception, ExceptionCatchBlocks.HttpServer)
            {
                Request = new HttpRequestMessage()
            };

            exceptionContext.Request.SetAuthorizationLevel(authLevel);
            exceptionContext.Request.SetConfiguration(_config);
            exceptionContext.Request.Properties.Add(ScriptConstants.AzureFunctionsRequestIdKey, requestId);
            exceptionContext.RequestContext = new System.Web.Http.Controllers.HttpRequestContext();

            var context = new ExceptionHandlerContext(exceptionContext);
            context.RequestContext.IncludeErrorDetail = includeDetails;

            handler.Handle(context);

            HttpResponseMessage response = await context.Result.ExecuteAsync(CancellationToken.None);

            ApiErrorModel error = await response.Content.ReadAsAsync<ApiErrorModel>();

            Assert.Equal(requestId, error.RequestId);

            return error;
        }
    }
}
