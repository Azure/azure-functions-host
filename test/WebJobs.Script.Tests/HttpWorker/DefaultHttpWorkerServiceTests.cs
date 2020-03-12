// Copyright (c) .NET Foundation. All rights reserved.
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
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Moq;
using Moq.Protected;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.HttpWorker
{
    public class DefaultHttpWorkerServiceTests
    {
        private const string TestFunctionName = "testFunctionName";
        private HttpClient _httpClient;
        private DefaultHttpWorkerService _defaultHttpWorkerService;
        private Guid _testInvocationId;
        private HttpWorkerOptions _httpWorkerOptions;
        private ILoggerFactory _testLoggerFactory;
        private int _defaultPort = 8090;
        private TestLogger _testLogger = new TestLogger("ServiceLogger");
        private TestLogger _functionLogger = new TestLogger(TestFunctionName);

        public DefaultHttpWorkerServiceTests()
        {
            _testLoggerFactory = new LoggerFactory();
            _testInvocationId = Guid.NewGuid();
            _httpWorkerOptions = new HttpWorkerOptions()
            {
                Port = _defaultPort
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
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger);
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
        public async Task ProcessDefaultInvocationRequest_DataType_Binary_Succeeds()
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((request, token) => ValidateDefaultInvocationRequest(request))
                .ReturnsAsync(HttpWorkerTestUtilities.GetValidHttpResponseMessage_DataType_Binary_Data());

            _httpClient = new HttpClient(handlerMock.Object);
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger);
            var testScriptInvocationContext = HttpWorkerTestUtilities.GetScriptInvocationContext(TestFunctionName, _testInvocationId, _functionLogger, Abstractions.Description.DataType.Binary);
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
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger);
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
        public async Task ProcessSimpleHttpTriggerInvocationRequest_Succeeds()
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((request, token) => ValidateSimpleHttpTriggerInvocationRequest(request))
                .ReturnsAsync(HttpWorkerTestUtilities.GetValidSimpleHttpResponseMessage());

            _httpClient = new HttpClient(handlerMock.Object);
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger);
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
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger);
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
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger);
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
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger);
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
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger);
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
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger);
            _defaultHttpWorkerService.ProcessLogsFromHttpResponse(HttpWorkerTestUtilities.GetScriptInvocationContext(TestFunctionName, _testInvocationId, _functionLogger), httpScriptInvocationResult);
            var testLogs = _functionLogger.GetLogMessages();
            if (httpScriptInvocationResult.Logs != null && httpScriptInvocationResult.Logs.Any())
            {
                Assert.True(testLogs.Count() == httpScriptInvocationResult.Logs?.Count());
                Assert.True(testLogs.All(m => m.FormattedMessage.Contains("test log")));
            }
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
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger);

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
            _defaultHttpWorkerService = new DefaultHttpWorkerService(_httpClient, new OptionsWrapper<HttpWorkerOptions>(_httpWorkerOptions), _testLogger);

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
