﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Moq;
using Moq.Protected;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.HttpWorker
{
    public class DefaultHttpWorkerServiceTests
    {
        private const string TestFunctionName = "testFunctionName";
        private HttpClient _httpClient;
        private TestEnvironment _testEnvironment;
        private DefaultHttpWorkerService _defaultHttpWorkerService;
        private Guid _testInvocationId;
        private HttpWorkerOptions _httpWorkerOptions;
        private ScriptJobHostOptions _scriptJobHostOptions;
        private ILoggerFactory _testLoggerFactory;
        private int _defaultPort = 8090;
        private TestLogger _testLogger = new TestLogger("ServiceLogger");
        private TestLogger _functionLogger = new TestLogger(TestFunctionName);

        public DefaultHttpWorkerServiceTests()
        {
            _testLoggerFactory = new LoggerFactory();
            _testEnvironment = new TestEnvironment();
            _testInvocationId = Guid.NewGuid();
            _httpWorkerOptions = new HttpWorkerOptions()
            {
                Port = _defaultPort,
                Type = CustomHandlerType.None
            };
            _scriptJobHostOptions = new ScriptJobHostOptions()
            {
                FunctionTimeout = TimeSpan.FromMinutes(15)
            };
        }

        public static IEnumerable<object[]> TestLogs
        {
            get
            {
                yield return new object[] { new HttpScriptInvocationResult() { Logs = new List<string>() { "test log1", "test log2" } } };
                yield return new object[] { new HttpScriptInvocationResult() };
            }
        }

        [Fact]
        public async Task ProcessDefaultInvocationRequest_Succeeds()
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((request, token) => ValidateDefaultInvocationRequest(request))
                .ReturnsAsync(HttpWorkerTestUtilities.GetValidHttpResponseMessage());

            _httpClient = new HttpClient(handlerMock.Object);
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger, _testEnvironment, new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions));
            Assert.Equal(_httpClient.Timeout, _scriptJobHostOptions.FunctionTimeout.Value.Add(TimeSpan.FromMinutes(1)));
            var testScriptInvocationContext = HttpWorkerTestUtilities.GetScriptInvocationContext(TestFunctionName, _testInvocationId, _functionLogger);
            await _defaultHttpWorkerService.ProcessDefaultInvocationRequest(testScriptInvocationContext);
            var invocationResult = await testScriptInvocationContext.ResultSource.Task;

            var expectedHttpScriptInvocationResult = HttpWorkerTestUtilities.GetTestHttpScriptInvocationResult();
            var testLogs = _functionLogger.GetLogMessages();
            Assert.True(testLogs.Count() == expectedHttpScriptInvocationResult.Logs.Count());
            Assert.True(testLogs.All(m => m.FormattedMessage.Contains("invocation log")));
            Assert.Equal(expectedHttpScriptInvocationResult.Outputs.Count(), invocationResult.Outputs.Count());
            Assert.Equal(expectedHttpScriptInvocationResult.ReturnValue, invocationResult.Return);
        }

        [Fact]
        public async Task ProcessDefaultInvocationRequest_CustomHandler_EnableRequestForwarding_False()
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            var customHandlerOptions = new HttpWorkerOptions()
            {
                Port = _defaultPort,
                Type = CustomHandlerType.Http
            };
            var scriptJobHostOptionsNoTimeout = new ScriptJobHostOptions();
            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((request, token) => ValidateeSimpleHttpTriggerSentAsDefaultInvocationRequest(request))
                .ReturnsAsync(HttpWorkerTestUtilities.GetValidHttpResponseMessageWithJsonRes());

            _httpClient = new HttpClient(handlerMock.Object);
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(customHandlerOptions), _testLogger, _testEnvironment, new OptionsWrapper<ScriptJobHostOptions>(scriptJobHostOptionsNoTimeout));
            Assert.Equal(_httpClient.Timeout, TimeSpan.FromMilliseconds(int.MaxValue));
            var testScriptInvocationContext = HttpWorkerTestUtilities.GetSimpleHttpTriggerScriptInvocationContext(TestFunctionName, _testInvocationId, _functionLogger);
            await _defaultHttpWorkerService.InvokeAsync(testScriptInvocationContext);
            var invocationResult = await testScriptInvocationContext.ResultSource.Task;

            var expectedHttpScriptInvocationResult = HttpWorkerTestUtilities.GetHttpScriptInvocationResultWithJsonRes();
            var testLogs = _functionLogger.GetLogMessages();
            Assert.True(testLogs.Count() == expectedHttpScriptInvocationResult.Logs.Count());
            Assert.True(testLogs.All(m => m.FormattedMessage.Contains("invocation log")));
            Assert.Equal(expectedHttpScriptInvocationResult.Outputs.Count(), invocationResult.Outputs.Count());
            Assert.Equal(expectedHttpScriptInvocationResult.ReturnValue, invocationResult.Return);
            var responseJson = JObject.Parse(invocationResult.Outputs["res"].ToString());
            Assert.Equal("my world", responseJson["Body"]);
            Assert.Equal("201", responseJson["StatusCode"]);
        }

        [Fact]
        public async Task ProcessDefaultInvocationRequest_DataType_Binary_Succeeds()
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((request, token) => ValidateDefaultInvocationRequest(request))
                .ReturnsAsync(HttpWorkerTestUtilities.GetValidHttpResponseMessage_DataType_Binary_Data());

            _httpClient = new HttpClient(handlerMock.Object);
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger, _testEnvironment, new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions));
            var testScriptInvocationContext = HttpWorkerTestUtilities.GetScriptInvocationContext(TestFunctionName, _testInvocationId, _functionLogger, WebJobs.Script.Description.DataType.Binary);
            await _defaultHttpWorkerService.ProcessDefaultInvocationRequest(testScriptInvocationContext);
            var invocationResult = await testScriptInvocationContext.ResultSource.Task;

            var expectedHttpScriptInvocationResult = HttpWorkerTestUtilities.GetTestHttpScriptInvocationResult_DataType_Binary_Outputs();
            var testLogs = _functionLogger.GetLogMessages();
            Assert.True(testLogs.Count() == expectedHttpScriptInvocationResult.Logs.Count());
            Assert.True(testLogs.All(m => m.FormattedMessage.Contains("invocation log")));
            Assert.Equal(expectedHttpScriptInvocationResult.Outputs.Count(), invocationResult.Outputs.Count());

            // Verifies output data is not transformed if dataType is set to binary
            foreach (var expectedOutput in expectedHttpScriptInvocationResult.Outputs)
            {
                string expectedOutputString = Encoding.UTF8.GetString((byte[])expectedOutput.Value);
                string actualOutputString = Encoding.UTF8.GetString((byte[])invocationResult.Outputs[expectedOutput.Key]);
                Assert.Equal(expectedOutputString, actualOutputString);
            }

            string expectedString = Encoding.UTF8.GetString((byte[])expectedHttpScriptInvocationResult.ReturnValue);
            string actualString = Encoding.UTF8.GetString((byte[])invocationResult.Return);
            Assert.Equal(expectedString, actualString);
        }

        [Fact]
        public async Task ProcessDefaultInvocationRequest_BinaryData_Succeeds()
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((request, token) => ValidateDefaultInvocationRequest(request))
                .ReturnsAsync(HttpWorkerTestUtilities.GetValidHttpResponseMessage_Binary_Data());

            _httpClient = new HttpClient(handlerMock.Object);
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger, _testEnvironment, new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions));
            var testScriptInvocationContext = HttpWorkerTestUtilities.GetScriptInvocationContext(TestFunctionName, _testInvocationId, _functionLogger);
            await _defaultHttpWorkerService.ProcessDefaultInvocationRequest(testScriptInvocationContext);
            var invocationResult = await testScriptInvocationContext.ResultSource.Task;

            var expectedHttpScriptInvocationResult = HttpWorkerTestUtilities.GetTestHttpScriptInvocationResult_Binary_Outputs();
            var testLogs = _functionLogger.GetLogMessages();
            Assert.True(testLogs.Count() == expectedHttpScriptInvocationResult.Logs.Count());
            Assert.True(testLogs.All(m => m.FormattedMessage.Contains("invocation log")));
            Assert.Equal(expectedHttpScriptInvocationResult.Outputs.Count(), invocationResult.Outputs.Count());

            // Verifies decoding for binary data from base64 encoded string
            foreach (var expectedOutput in expectedHttpScriptInvocationResult.Outputs)
            {
                byte[] expectedOutputData = Convert.FromBase64String((string)expectedOutput.Value);
                string expectedOutputString = Encoding.UTF8.GetString(expectedOutputData);
                string actualOutputString = Encoding.UTF8.GetString((byte[])invocationResult.Outputs[expectedOutput.Key]);
                Assert.Equal(expectedOutputString, actualOutputString);
            }

            byte[] expectedStringData = Convert.FromBase64String((string)expectedHttpScriptInvocationResult.ReturnValue);
            string expectedString = Encoding.UTF8.GetString(expectedStringData);
            string actualString = Encoding.UTF8.GetString((byte[])invocationResult.Return);
            Assert.Equal(expectedString, actualString);
        }

        [Fact]
        public async Task ProcessPing_Succeeds()
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((request, token) => ValidateSimpleHttpTriggerInvocationRequest(request))
                .ReturnsAsync(HttpWorkerTestUtilities.GetSimpleNotFoundHttpResponseMessage());

            _httpClient = new HttpClient(handlerMock.Object);
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger, _testEnvironment, new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions));
            await _defaultHttpWorkerService.PingAsync();
            handlerMock.VerifyAll();
        }

        [Fact]
        public async Task ProcessSimpleHttpTriggerInvocationRequest_Succeeds()
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((request, token) => ValidateSimpleHttpTriggerInvocationRequest(request))
                .ReturnsAsync(HttpWorkerTestUtilities.GetValidSimpleHttpResponseMessage());

            _httpClient = new HttpClient(handlerMock.Object);
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger, _testEnvironment, new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions));
            var testScriptInvocationContext = HttpWorkerTestUtilities.GetSimpleHttpTriggerScriptInvocationContext(TestFunctionName, _testInvocationId, _functionLogger);
            await _defaultHttpWorkerService.ProcessHttpInAndOutInvocationRequest(testScriptInvocationContext);
            var invocationResult = await testScriptInvocationContext.ResultSource.Task;
            var expectedHttpResponseMessage = HttpWorkerTestUtilities.GetValidSimpleHttpResponseMessage();
            var expectedResponseContent = await expectedHttpResponseMessage.Content.ReadAsStringAsync();

            var testLogs = _functionLogger.GetLogMessages();
            Assert.Equal(0, testLogs.Count());

            Assert.Equal(1, invocationResult.Outputs.Count());
            var httpOutputResponse = invocationResult.Outputs.FirstOrDefault().Value as HttpResponseMessage;
            Assert.NotNull(httpOutputResponse);
            Assert.Equal(expectedHttpResponseMessage.StatusCode, httpOutputResponse.StatusCode);
            Assert.Equal(expectedResponseContent, await httpOutputResponse.Content.ReadAsStringAsync());

            var response = invocationResult.Return as HttpResponseMessage;
            Assert.NotNull(response);
            Assert.Equal(expectedHttpResponseMessage.StatusCode, response.StatusCode);
            Assert.Equal(expectedResponseContent, await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ProcessSimpleHttpTriggerInvocationRequest_CustomHandler_EnableForwardingHttpRequest_True()
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            var customHandlerOptions = new HttpWorkerOptions()
            {
                Port = _defaultPort,
                EnableForwardingHttpRequest = true
            };
            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((request, token) => ValidateSimpleHttpTriggerInvocationRequest(request))
                .ReturnsAsync(HttpWorkerTestUtilities.GetValidSimpleHttpResponseMessage());

            _httpClient = new HttpClient(handlerMock.Object);
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(customHandlerOptions), _testLogger, _testEnvironment, new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions));
            var testScriptInvocationContext = HttpWorkerTestUtilities.GetSimpleHttpTriggerScriptInvocationContext(TestFunctionName, _testInvocationId, _functionLogger);
            await _defaultHttpWorkerService.InvokeAsync(testScriptInvocationContext);
            var invocationResult = await testScriptInvocationContext.ResultSource.Task;
            var expectedHttpResponseMessage = HttpWorkerTestUtilities.GetValidSimpleHttpResponseMessage();
            var expectedResponseContent = await expectedHttpResponseMessage.Content.ReadAsStringAsync();

            var testLogs = _functionLogger.GetLogMessages();
            Assert.Equal(0, testLogs.Count());

            Assert.Equal(1, invocationResult.Outputs.Count());
            var httpOutputResponse = invocationResult.Outputs.FirstOrDefault().Value as HttpResponseMessage;
            Assert.NotNull(httpOutputResponse);
            Assert.Equal(expectedHttpResponseMessage.StatusCode, httpOutputResponse.StatusCode);
            Assert.Equal(expectedResponseContent, await httpOutputResponse.Content.ReadAsStringAsync());

            var response = invocationResult.Return as HttpResponseMessage;
            Assert.NotNull(response);
            Assert.Equal(expectedHttpResponseMessage.StatusCode, response.StatusCode);
            Assert.Equal(expectedResponseContent, await response.Content.ReadAsStringAsync());
        }

        [Theory]
        [InlineData("somePathValue", "http://127.0.0.1:8080/somePathValue")]
        [InlineData("", "http://127.0.0.1:8080/")]
        public void TestBuildAndGetUri(string pathValue, string expectedUriString)
        {
            HttpWorkerOptions testOptions = new HttpWorkerOptions
            {
                Port = 8080,
            };
            DefaultHttpWorkerService defaultHttpWorkerService = new DefaultHttpWorkerService(new HttpClient(), new OptionsWrapper<HttpWorkerOptions>(testOptions), _testLogger, _testEnvironment, new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions));
            Assert.Equal(expectedUriString, defaultHttpWorkerService.BuildAndGetUri(pathValue));
        }

        [Fact]
        public void AddHeadersTest()
        {
            HttpWorkerOptions testOptions = new HttpWorkerOptions();
            DefaultHttpWorkerService defaultHttpWorkerService = new DefaultHttpWorkerService(new HttpClient(), new OptionsWrapper<HttpWorkerOptions>(testOptions), _testLogger, _testEnvironment, new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions));
            HttpRequestMessage input = new HttpRequestMessage();
            string invocationId = Guid.NewGuid().ToString();

            defaultHttpWorkerService.AddHeaders(input, invocationId);
            Assert.Equal(input.Headers.GetValues(HttpWorkerConstants.HostVersionHeaderName).FirstOrDefault(), ScriptHost.Version);
            Assert.Equal(input.Headers.GetValues(HttpWorkerConstants.InvocationIdHeaderName).FirstOrDefault(), invocationId);
        }

        [Fact]
        public async Task ProcessSimpleHttpTriggerInvocationRequest_Sets_ExpectedResult()
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((request, token) => RequestHandler(request))
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.InternalServerError
                });

            _httpClient = new HttpClient(handlerMock.Object);
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger, _testEnvironment, new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions));
            var testScriptInvocationContext = HttpWorkerTestUtilities.GetSimpleHttpTriggerScriptInvocationContext(TestFunctionName, _testInvocationId, _functionLogger);
            await _defaultHttpWorkerService.ProcessHttpInAndOutInvocationRequest(testScriptInvocationContext);
            var invocationResult = await testScriptInvocationContext.ResultSource.Task;
            var expectedHttpResponseMessage = HttpWorkerTestUtilities.GetValidSimpleHttpResponseMessage();
            var expectedResponseContent = await expectedHttpResponseMessage.Content.ReadAsStringAsync();

            var testLogs = _functionLogger.GetLogMessages();
            Assert.Equal(0, testLogs.Count());

            Assert.Equal(1, invocationResult.Outputs.Count());
            var httpOutputResponse = invocationResult.Outputs.FirstOrDefault().Value as HttpResponseMessage;
            Assert.NotNull(httpOutputResponse);
            Assert.Equal(HttpStatusCode.InternalServerError, httpOutputResponse.StatusCode);

            var response = invocationResult.Return as HttpResponseMessage;
            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        [Fact]
        public async Task ProcessDefaultInvocationRequest_JsonResponse_Succeeds()
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((request, token) => RequestHandler(request))
                .ReturnsAsync(HttpWorkerTestUtilities.GetHttpResponseMessageWithJsonContent());

            _httpClient = new HttpClient(handlerMock.Object);
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger, _testEnvironment, new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions));
            var testScriptInvocationContext = HttpWorkerTestUtilities.GetScriptInvocationContext(TestFunctionName, _testInvocationId, _functionLogger);
            await _defaultHttpWorkerService.ProcessDefaultInvocationRequest(testScriptInvocationContext);
            var invocationResult = await testScriptInvocationContext.ResultSource.Task;
            Assert.Empty(invocationResult.Outputs);
            Assert.Null(invocationResult.Return);
        }

        [Fact]
        public async Task ProcessDefaultInvocationRequest_InvalidMediaType_Throws()
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((request, token) => RequestHandler(request))
                .ReturnsAsync(HttpWorkerTestUtilities.GetHttpResponseMessageWithStringContent());

            _httpClient = new HttpClient(handlerMock.Object);
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger, _testEnvironment, new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions));
            var testScriptInvocationContext = HttpWorkerTestUtilities.GetScriptInvocationContext(TestFunctionName, _testInvocationId, _testLogger);
            await _defaultHttpWorkerService.ProcessDefaultInvocationRequest(testScriptInvocationContext);
            await Assert.ThrowsAsync<UnsupportedMediaTypeException>(async () => await testScriptInvocationContext.ResultSource.Task);
        }

        [Fact]
        public async Task ProcessDefaultInvocationRequest_BadRequestResponse_Throws()
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((request, token) => RequestHandler(request))
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.BadRequest
                });

            _httpClient = new HttpClient(handlerMock.Object);
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger, _testEnvironment, new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions));
            var testScriptInvocationContext = HttpWorkerTestUtilities.GetScriptInvocationContext(TestFunctionName, _testInvocationId, _testLogger);
            await _defaultHttpWorkerService.ProcessDefaultInvocationRequest(testScriptInvocationContext);
            await Assert.ThrowsAsync<HttpRequestException>(async () => await testScriptInvocationContext.ResultSource.Task);
        }

        [Theory]
        [MemberData(nameof(TestLogs))]
        public void ProcessOutputLogs_Succeeds(HttpScriptInvocationResult httpScriptInvocationResult)
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((request, token) => RequestHandler(request))
                .ReturnsAsync(HttpWorkerTestUtilities.GetValidHttpResponseMessage());

            _httpClient = new HttpClient(handlerMock.Object);
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger, _testEnvironment, new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions));
            _defaultHttpWorkerService.ProcessLogsFromHttpResponse(HttpWorkerTestUtilities.GetScriptInvocationContext(TestFunctionName, _testInvocationId, _functionLogger), httpScriptInvocationResult);
            var testLogs = _functionLogger.GetLogMessages();
            if (httpScriptInvocationResult.Logs != null && httpScriptInvocationResult.Logs.Any())
            {
                Assert.True(testLogs.Count() == httpScriptInvocationResult.Logs?.Count());
                Assert.True(testLogs.All(m => m.FormattedMessage.Contains("test log")));
            }
        }

        [Theory]
        [InlineData("someFuntionName", CustomHandlerType.Http, true, "/")]
        [InlineData("someFuntionName", CustomHandlerType.Http, true, "/api/hello", "localhost/api/hello")]
        [InlineData("someFuntionName", CustomHandlerType.Http, false, "someFuntionName")]
        [InlineData("someFuntionName", CustomHandlerType.Http, false, "someFuntionName", "localhost/api/hello")]
        [InlineData("someFuntionName", CustomHandlerType.None, true, "someFuntionName")]
        [InlineData("someFuntionName", CustomHandlerType.None, true, "someFuntionName", "localhost/api/hello")]
        [InlineData("someFuntionName", CustomHandlerType.None, false, "someFuntionName")]
        [InlineData("someFuntionName", CustomHandlerType.None, false, "someFuntionName", "localhost/api/hello")]
        public void TestPathValue(string functionName, CustomHandlerType type, bool enableForwardingHttpRequest, string expectedValue, string hostValue = "localhost")
        {
            HttpRequest testHttpRequest = HttpWorkerTestUtilities.GetTestHttpRequest(hostValue);
            HttpWorkerOptions testOptions = new HttpWorkerOptions
            {
                Type = type,
                EnableForwardingHttpRequest = enableForwardingHttpRequest,
            };
            DefaultHttpWorkerService defaultHttpWorkerService = new DefaultHttpWorkerService(new HttpClient(), new OptionsWrapper<HttpWorkerOptions>(testOptions), _testLogger, _testEnvironment, new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions));
            string actualValue = defaultHttpWorkerService.GetPathValue(testOptions, functionName, testHttpRequest);
            Assert.Equal(actualValue, expectedValue);
        }

        [Fact]
        public async Task IsWorkerReady_Returns_False()
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((request, token) => RequestHandler(request))
                .Throws(new HttpRequestException("Invalid http worker service", new SocketException()));

            _httpClient = new HttpClient(handlerMock.Object);
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger, _testEnvironment, new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions));

            bool workerReady = await _defaultHttpWorkerService.IsWorkerReady(CancellationToken.None);
            Assert.False(workerReady);

            var testLogs = _testLogger.GetLogMessages();
            Assert.True(testLogs.All(m => m.FormattedMessage.Contains("Invalid http worker service")));
        }

        [Fact]
        public async Task IsWorkerReady_Returns_True()
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((request, token) => RequestHandler(request))
                .ReturnsAsync(HttpWorkerTestUtilities.GetValidHttpResponseMessage());

            _httpClient = new HttpClient(handlerMock.Object);
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger, _testEnvironment, new OptionsWrapper<ScriptJobHostOptions>(_scriptJobHostOptions));

            bool workerReady = await _defaultHttpWorkerService.IsWorkerReady(CancellationToken.None);
            Assert.True(workerReady);
        }

        private async void ValidateDefaultInvocationRequest(HttpRequestMessage httpRequestMessage)
        {
            Assert.Contains($"{HttpWorkerConstants.UserAgentHeaderValue}/{ScriptHost.Version}", httpRequestMessage.Headers.UserAgent.ToString());
            Assert.Equal(_testInvocationId.ToString(), httpRequestMessage.Headers.GetValues(HttpWorkerConstants.InvocationIdHeaderName).Single());
            Assert.Equal(ScriptHost.Version, httpRequestMessage.Headers.GetValues(HttpWorkerConstants.HostVersionHeaderName).Single());
            Assert.Equal(httpRequestMessage.RequestUri.ToString(), $"http://127.0.0.1:{_defaultPort}/{TestFunctionName}");

            HttpScriptInvocationContext httpScriptInvocationContext = await httpRequestMessage.Content.ReadAsAsync<HttpScriptInvocationContext>();

            // Verify Metadata
            var expectedMetadata = HttpWorkerTestUtilities.GetScriptInvocationBindingData();
            Assert.Equal(expectedMetadata.Count(), httpScriptInvocationContext.Metadata.Count());
            foreach (var key in expectedMetadata.Keys)
            {
                Assert.Equal(JsonConvert.SerializeObject(expectedMetadata[key]), httpScriptInvocationContext.Metadata[key]);
            }

            // Verify Data
            var expectedData = HttpWorkerTestUtilities.GetScriptInvocationInputs();
            Assert.Equal(expectedData.Count(), httpScriptInvocationContext.Data.Count());
            foreach (var item in expectedData)
            {
                Assert.True(httpScriptInvocationContext.Data.Keys.Contains(item.name));
                Assert.Equal(JsonConvert.SerializeObject(item.val), httpScriptInvocationContext.Data[item.name]);
            }
        }

        private async void ValidateeSimpleHttpTriggerSentAsDefaultInvocationRequest(HttpRequestMessage httpRequestMessage)
        {
            Assert.Contains($"{HttpWorkerConstants.UserAgentHeaderValue}/{ScriptHost.Version}", httpRequestMessage.Headers.UserAgent.ToString());
            Assert.Equal(_testInvocationId.ToString(), httpRequestMessage.Headers.GetValues(HttpWorkerConstants.InvocationIdHeaderName).Single());
            Assert.Equal(ScriptHost.Version, httpRequestMessage.Headers.GetValues(HttpWorkerConstants.HostVersionHeaderName).Single());
            Assert.Equal(httpRequestMessage.RequestUri.ToString(), $"http://127.0.0.1:{_defaultPort}/{TestFunctionName}");

            HttpScriptInvocationContext httpScriptInvocationContext = await httpRequestMessage.Content.ReadAsAsync<HttpScriptInvocationContext>();

            // Verify Metadata
            var expectedMetadata = HttpWorkerTestUtilities.GetScriptInvocationBindingData();
            Assert.Equal(expectedMetadata.Count(), httpScriptInvocationContext.Metadata.Count());
            foreach (var key in expectedMetadata.Keys)
            {
                Assert.Equal(JsonConvert.SerializeObject(expectedMetadata[key]), httpScriptInvocationContext.Metadata[key]);
            }

            // Verify Data
            Assert.True(httpScriptInvocationContext.Data.Keys.Contains("testInputReq"));
            JObject resultHttpReq = JObject.FromObject(httpScriptInvocationContext.Data["testInputReq"]);
            JObject expectedHttpRequest = await HttpWorkerTestUtilities.GetTestHttpRequest().GetRequestAsJObject();
            Assert.True(JToken.DeepEquals(expectedHttpRequest, resultHttpReq));
        }

        private async void ValidateSimpleHttpTriggerInvocationRequest(HttpRequestMessage httpRequestMessage)
        {
            Assert.Contains($"{HttpWorkerConstants.UserAgentHeaderValue}/{ScriptHost.Version}", httpRequestMessage.Headers.UserAgent.ToString());
            Assert.Equal(_testInvocationId.ToString(), httpRequestMessage.Headers.GetValues(HttpWorkerConstants.InvocationIdHeaderName).Single());
            Assert.Equal(ScriptHost.Version, httpRequestMessage.Headers.GetValues(HttpWorkerConstants.HostVersionHeaderName).Single());
            Assert.Equal($"http://127.0.0.1:{_defaultPort}/{TestFunctionName}{HttpWorkerTestUtilities.QueryParamString}", httpRequestMessage.RequestUri.AbsoluteUri);
            Assert.Equal(HttpWorkerTestUtilities.AcceptHeaderValue, httpRequestMessage.Headers.GetValues(HeaderNames.Accept).FirstOrDefault());
            Assert.Equal(HttpWorkerTestUtilities.UTF8AcceptCharset, httpRequestMessage.Headers.GetValues(HeaderNames.AcceptCharset).FirstOrDefault());
            var content = await httpRequestMessage.Content.ReadAsStringAsync();
            Assert.Equal($"\"{HttpWorkerTestUtilities.HttpContentStringValue}\"", content);
        }

        private void RequestHandler(HttpRequestMessage httpRequestMessage)
        {
            //used for tests that do not need request validation
        }
    }
}
