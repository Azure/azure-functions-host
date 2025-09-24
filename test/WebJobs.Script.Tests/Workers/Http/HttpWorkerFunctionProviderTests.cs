// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
        private static IOptions<HttpWorkerOptions> CreateHttpWorkerOptions(IEnumerable<HttpWorkerRoute> routes = null, bool customRoutesEnabled = true, bool includeHttpSection = true)
        {
            var options = new HttpWorkerOptions
            {
                CustomRoutesEnabled = customRoutesEnabled
            };

            if (includeHttpSection)
            {
                options.Http = new CustomHandlerHttpOptions
                {
                    Routes = routes
                };
            }

            return Options.Create(options);
        }

        private static TestOptionsMonitor<LanguageWorkerOptions> CreateLanguageWorkerOptions()
        {
            var lang = new LanguageWorkerOptions
            {
                WorkerConfigs = []
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
                new NullLogger<HttpWorkerFunctionProvider>());
        }

        [Fact]
        public async Task GetFunctionMetadataAsync_NoHttpSection_ReturnsEmpty()
        {
            var hostMeta = new Mock<IHostFunctionMetadataProvider>(MockBehavior.Strict);
            var provider = CreateProvider(
                CreateHttpWorkerOptions(routes: null, customRoutesEnabled: true, includeHttpSection: false),
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
                CreateHttpWorkerOptions(null),
                hostMeta.Object);

            var result = await provider.GetFunctionMetadataAsync();

            Assert.Empty(result);
            hostMeta.Verify(m => m.GetFunctionMetadataAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), false), Times.Never);
        }

        [Fact]
        public async Task GetFunctionMetadataAsync_CustomRoutesDisabled_NoRoutes_ReturnsEmpty_NoException()
        {
            var hostMeta = new Mock<IHostFunctionMetadataProvider>(MockBehavior.Strict);
            var provider = CreateProvider(
                CreateHttpWorkerOptions(null, customRoutesEnabled: false),
                hostMeta.Object);

            var result = await provider.GetFunctionMetadataAsync();

            Assert.Empty(result);
            hostMeta.Verify(m => m.GetFunctionMetadataAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), false), Times.Never);
        }

        [Fact]
        public async Task GetFunctionMetadataAsync_CustomRoutesDisabled_WithRoutes_Throws()
        {
            var hostMeta = new Mock<IHostFunctionMetadataProvider>(MockBehavior.Strict);
            var routes = new[]
            {
                new HttpWorkerRoute
                {
                    Route = "/a",
                    AuthorizationLevel = AuthorizationLevel.Function
                }
            };
            var provider = CreateProvider(
                CreateHttpWorkerOptions(routes, customRoutesEnabled: false),
                hostMeta.Object);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetFunctionMetadataAsync());
            Assert.Equal("Routes configuration is only allowed for worker runtime: custom", ex.Message);
            hostMeta.Verify(m => m.GetFunctionMetadataAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), false), Times.Never);
        }

        [Fact]
        public async Task GetFunctionMetadataAsync_MixedSources_Throws()
        {
            var existing = ImmutableArray.Create(new FunctionMetadata { Name = "existing" });

            var hostMeta = new Mock<IHostFunctionMetadataProvider>(MockBehavior.Strict);
            hostMeta.Setup(m => m.GetFunctionMetadataAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), false))
                .ReturnsAsync(existing);

            var routes = new[]
            {
                new HttpWorkerRoute
                {
                    Route = "/a",
                    AuthorizationLevel = AuthorizationLevel.Function
                }
            };
            var provider = CreateProvider(
                CreateHttpWorkerOptions(routes),
                hostMeta.Object);

            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetFunctionMetadataAsync());

            hostMeta.Verify(m => m.GetFunctionMetadataAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), false), Times.Once);
        }

        [Fact]
        public async Task GetFunctionMetadataAsync_ConfiguredRoutes_CreatesFunctions()
        {
            var hostMeta = new Mock<IHostFunctionMetadataProvider>(MockBehavior.Strict);
            hostMeta.Setup(m => m.GetFunctionMetadataAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), false))
                .ReturnsAsync([]);

            var routes = new[]
            {
                new HttpWorkerRoute
                {
                    Route = "/one",
                    AuthorizationLevel = AuthorizationLevel.Function
                },
                new HttpWorkerRoute
                {
                    Route = "/two/{id}",
                    AuthorizationLevel = AuthorizationLevel.Function
                }
            };

            var provider = CreateProvider(
                CreateHttpWorkerOptions(routes),
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
                .ReturnsAsync([]);

            var routes = new[]
            {
                new HttpWorkerRoute
                {
                    Route = "/ok/{id}",
                    AuthorizationLevel = AuthorizationLevel.Function
                },
                new HttpWorkerRoute
                {
                    Route = "/bad//slash",
                    AuthorizationLevel = AuthorizationLevel.Function
                },
                new HttpWorkerRoute
                {
                    Route = "/empty/{}",
                    AuthorizationLevel = AuthorizationLevel.Function
                }
            };

            var provider = CreateProvider(
                CreateHttpWorkerOptions(routes),
                hostMeta.Object);

            var result = await provider.GetFunctionMetadataAsync();

            Assert.Single(result);
            Assert.Equal("http-handler1", result[0].Name);

            var errors = provider.FunctionErrors;
            Assert.True(errors.ContainsKey("http-handler2"));
            Assert.True(errors.ContainsKey("http-handler3"));

            Assert.Contains("cannot appear consecutively", errors["http-handler2"].First(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("invalid", errors["http-handler3"].First(), StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("/simple", true, null)]
        [InlineData("/with space", true, null)]
        [InlineData("/double//slash", false, "cannot appear consecutively")]
        [InlineData("", false, "Route cannot be null, empty or whitespace.")]
        [InlineData(null, false, "Route cannot be null, empty or whitespace.")]
        [InlineData(" ", false, "Route cannot be null, empty or whitespace.")]
        [InlineData("/empty/{}", false, "invalid")]
        [InlineData("/param/{name}", true, null)]
        [InlineData("/unbalanced/{name", false, "incomplete parameter")]
        [InlineData("/too/many/close}", false, "incomplete parameter")]
        public async Task RouteValidation_Patterns(string route, bool expectedSuccess, string expectedErrorSubstring)
        {
            var hostMeta = new Mock<IHostFunctionMetadataProvider>(MockBehavior.Strict);
            hostMeta.Setup(m => m.GetFunctionMetadataAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), false))
                .ReturnsAsync([]);

            var routes = new[]
            {
                new HttpWorkerRoute
                {
                    Route = route,
                    AuthorizationLevel = AuthorizationLevel.Function
                }
            };
            var provider = CreateProvider(
                CreateHttpWorkerOptions(routes),
                hostMeta.Object);

            var result = await provider.GetFunctionMetadataAsync();

            if (expectedSuccess)
            {
                Assert.Single(result);
                Assert.Empty(provider.FunctionErrors);
            }
            else
            {
                Assert.Empty(result);
                var errors = provider.FunctionErrors;
                Assert.True(errors.ContainsKey("http-handler1"));
                var msg = Assert.Single(errors["http-handler1"]);
                Assert.Contains(expectedErrorSubstring, msg, StringComparison.OrdinalIgnoreCase);
            }
        }

        private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
        {
            private readonly T _value;

            public TestOptionsMonitor(T value) => _value = value;

            public T CurrentValue => _value;

            public T Get(string name) => _value;

            public IDisposable OnChange(Action<T, string> listener) => new Dummy();

            private sealed class Dummy : IDisposable
            {
                public void Dispose()
                {
                }
            }
        }
    }
}