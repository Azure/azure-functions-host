// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Http
{
    public class HttpWorkerFunctionProviderTests
    {
        private static IOptions<HttpWorkerOptions> CreateHttpWorkerOptions(string runtime, IEnumerable<HttpWorkerRoute> routes = null)
        {
            return Options.Create(new HttpWorkerOptions
            {
                WorkerRuntime = runtime,
                HttpRoutes = routes
            });
        }

        private static IOptionsMonitor<LanguageWorkerOptions> CreateLanguageWorkerOptions()
        {
            var lang = new LanguageWorkerOptions
            {
                WorkerConfigs = new List<RpcWorkerConfig>()
            };
            return new TestOptionsMonitor<LanguageWorkerOptions>(lang);
        }

        private static HttpWorkerFunctionProvider CreateProvider(
            IOptions<HttpWorkerOptions> httpOptions,
            IHostFunctionMetadataProvider hostMetadataProvider)
        {
            return new HttpWorkerFunctionProvider(
                httpOptions,
                CreateLanguageWorkerOptions(),
                hostMetadataProvider,
                new TestEnvironment(),
                new NullLogger<HttpWorkerFunctionProvider>());
        }

        [Fact]
        public async Task GetFunctionMetadataAsync_RuntimeNotCustom_ReturnsEmpty()
        {
            var hostMeta = new Mock<IHostFunctionMetadataProvider>(MockBehavior.Strict);
            var provider = CreateProvider(
                CreateHttpWorkerOptions("dotnet-isolated"),
                hostMeta.Object);

            var result = await provider.GetFunctionMetadataAsync();

            Assert.Empty(result);
            hostMeta.Verify(m => m.GetFunctionMetadataAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), false), Times.Never);
        }

        [Fact]
        public async Task GetFunctionMetadataAsync_NoRoutes_ReturnsEmpty()
        {
            var hostMeta = new Mock<IHostFunctionMetadataProvider>(MockBehavior.Strict);
            var provider = CreateProvider(
                CreateHttpWorkerOptions(ScriptConstants.CustomHandlerWorkerRuntime, null),
                hostMeta.Object);

            var result = await provider.GetFunctionMetadataAsync();

            Assert.Empty(result);
            hostMeta.Verify(m => m.GetFunctionMetadataAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), false), Times.Never);
        }

        [Fact]
        public async Task GetFunctionMetadataAsync_MixedSources_Throws()
        {
            var existing = ImmutableArray.Create(new FunctionMetadata { Name = "existing" });

            var hostMeta = new Mock<IHostFunctionMetadataProvider>(MockBehavior.Strict);
            hostMeta.Setup(m => m.GetFunctionMetadataAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), false))
                .ReturnsAsync(existing);

            var routes = new[] { new HttpWorkerRoute("/a", AuthorizationLevel.Function) };
            var provider = CreateProvider(
                CreateHttpWorkerOptions(ScriptConstants.CustomHandlerWorkerRuntime, routes),
                hostMeta.Object);

            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetFunctionMetadataAsync());

            hostMeta.Verify(m => m.GetFunctionMetadataAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), false), Times.Once);
        }

        [Fact]
        public async Task GetFunctionMetadataAsync_ConfiguredRoutes_CreatesFunctions()
        {
            var hostMeta = new Mock<IHostFunctionMetadataProvider>(MockBehavior.Strict);
            hostMeta.Setup(m => m.GetFunctionMetadataAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), false))
                .ReturnsAsync(ImmutableArray<FunctionMetadata>.Empty);

            var routes = new[]
            {
                new HttpWorkerRoute("/one", AuthorizationLevel.Function),
                new HttpWorkerRoute("/two/{id}", AuthorizationLevel.Function),
            };

            var provider = CreateProvider(
                CreateHttpWorkerOptions(ScriptConstants.CustomHandlerWorkerRuntime, routes),
                hostMeta.Object);

            var result = await provider.GetFunctionMetadataAsync();

            Assert.Equal(2, result.Length);
            Assert.Equal("http-handler1", result[0].Name);
            Assert.Equal("http-handler2", result[1].Name);

            var trigger1 = result[0].Bindings.Single(b => (string)b.Raw?["type"] == "httpTrigger");
            var trigger2 = result[1].Bindings.Single(b => (string)b.Raw?["type"] == "httpTrigger");

            Assert.Equal("/one", (string)trigger1.Raw["route"]);
            Assert.Equal("/two/{id}", (string)trigger2.Raw["route"]);

            var methods = (JArray)trigger1.Raw["methods"];
            Assert.Contains("get", methods.Select(m => m.ToString()), StringComparer.OrdinalIgnoreCase);

            hostMeta.Verify(m => m.GetFunctionMetadataAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), false), Times.Once);
        }

        [Fact]
        public async Task GetFunctionMetadataAsync_InvalidRoutes_SkippedAndErrorsCollected()
        {
            var hostMeta = new Mock<IHostFunctionMetadataProvider>(MockBehavior.Strict);
            hostMeta.Setup(m => m.GetFunctionMetadataAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), false))
                .ReturnsAsync(ImmutableArray<FunctionMetadata>.Empty);

            var routes = new[]
            {
                new HttpWorkerRoute("/ok/{id}", AuthorizationLevel.Function),
                new HttpWorkerRoute("/bad//slash", AuthorizationLevel.Function),
                new HttpWorkerRoute("/also {bad}", AuthorizationLevel.Function),
                new HttpWorkerRoute("/empty/{}", AuthorizationLevel.Function)
            };

            var provider = CreateProvider(
                CreateHttpWorkerOptions(ScriptConstants.CustomHandlerWorkerRuntime, routes),
                hostMeta.Object);

            var result = await provider.GetFunctionMetadataAsync();

            // Only the first one should be valid
            Assert.Single(result);
            Assert.Equal("http-handler1", result[0].Name);

            var errors = provider.FunctionErrors;
            Assert.True(errors.ContainsKey("http-handler2"));
            Assert.True(errors.ContainsKey("http-handler3"));
            Assert.True(errors.ContainsKey("http-handler4"));
            Assert.Contains("consecutive '/'", errors["http-handler2"].First(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("spaces", errors["http-handler3"].First(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("empty parameter", errors["http-handler4"].First(), StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("/simple", true, null)]
        [InlineData("/with space", false, "Route template cannot contain spaces.")]
        [InlineData("/double//slash", false, "Route template cannot contain consecutive '/'.")]
        [InlineData("", false, "Route template cannot be null or empty.")]
        [InlineData(null, false, "Route template cannot be null or empty.")]
        [InlineData("/empty/{}", false, "Route template contains an empty parameter '{}'.")]
        [InlineData("/param/{name}", true, null)]
        [InlineData("/unbalanced/{name", false, "Route template contains unmatched '{'.")]
        [InlineData("/too/many/close}", false, "Route template contains unmatched closing brace '}'.")]
        public void TryValidateHttpRoute_Patterns(string route, bool expectedSuccess, string expectedError)
        {
            var (success, error) = InvokeTryValidateHttpRoute(route);
            Assert.Equal(expectedSuccess, success);
            if (expectedSuccess)
            {
                Assert.Null(error);
            }
            else
            {
                Assert.Equal(expectedError, error);
            }
        }

        private static (bool Success, string Error) InvokeTryValidateHttpRoute(string route)
        {
            var method = typeof(HttpWorkerFunctionProvider)
                .GetMethod("TryValidateHttpRoute", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);

            object[] parameters = new object[] { route, null };
            bool result = (bool)method.Invoke(null, parameters);
            string error = (string)parameters[1];
            return (result, error);
        }

        private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
        {
            private readonly T _value;

            public TestOptionsMonitor(T value) => _value = value;

            public T CurrentValue => _value;

            public T Get(string name) => _value;

            public IDisposable OnChange(Action<T, string> listener) => new Dummy();

            private sealed class Dummy : IDisposable { public void Dispose() { } }
        }
    }
}