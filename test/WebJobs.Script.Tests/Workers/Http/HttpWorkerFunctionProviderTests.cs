// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
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

            var routes = new[] { new HttpWorkerRoute("/a", Microsoft.Azure.WebJobs.Extensions.Http.AuthorizationLevel.Function) };
            var provider = CreateProvider(
                CreateHttpWorkerOptions(ScriptConstants.CustomHandlerWorkerRuntime, routes),
                hostMeta.Object);

            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetFunctionMetadataAsync());

            hostMeta.Verify(m => m.GetFunctionMetadataAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), false), Times.Once);
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

        [Theory]
        [InlineData("/simple", true, null)]
        [InlineData("/with space", false, "Route template cannot contain spaces.")]
        [InlineData("/double//slash", false, "Route template cannot contain consecutive '/'.")]
        [InlineData("{unbalanced", false, "Route template contains unmatched '{'.")]
        [InlineData("/empty/{}", false, "Route template contains an empty parameter '{}'.")]
        [InlineData("/ok/{param}/more", true, null)]
        public void TryValidateHttpRoute_ReturnsExpected(string route, bool expectedSuccess, string expectedError)
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

        [Fact]
        public void TryValidateHttpRoute_NullRoute_CurrentImplementationThrows()
        {
            // Current implementation does not null-check before calling Contains, so null leads to a NullReferenceException.
            var method = typeof(HttpWorkerFunctionProvider)
                .GetMethod("TryValidateHttpRoute", BindingFlags.NonPublic | BindingFlags.Static);

            object[] parameters = new object[] { null, null };
            Assert.Throws<TargetInvocationException>(() => method.Invoke(null, parameters));
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