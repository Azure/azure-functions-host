// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Rpc.Core.Internal;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class ExtensionsCompositeEndpointDataSourceTests
    {
        private static readonly ILogger<ExtensionsCompositeEndpointDataSource> _dataSourceLogger
            = NullLogger<ExtensionsCompositeEndpointDataSource>.Instance;

        private static readonly ILogger<ExtensionsCompositeEndpointDataSource.EnsureInitializedMiddleware> _middlewareLogger
            = NullLogger<ExtensionsCompositeEndpointDataSource.EnsureInitializedMiddleware>.Instance;

        [Fact]
        public void NoActiveHost_NoEndpoints()
        {
            ExtensionsCompositeEndpointDataSource dataSource = new(Mock.Of<IScriptHostManager>(), _dataSourceLogger);
            Assert.Empty(dataSource.Endpoints);
        }

        [Fact]
        public void ActiveHostChanged_NullHost_EndpointsRemain()
        {
            Mock<IScriptHostManager> manager = new();
            ExtensionsCompositeEndpointDataSource dataSource = new(manager.Object, _dataSourceLogger);

            IChangeToken token = dataSource.GetChangeToken();
            Assert.False(token.HasChanged);
            manager.Raise(x => x.ActiveHostChanged += null, new ActiveHostChangedEventArgs(null, null));
            Assert.False(token.HasChanged);
        }

        [Fact]
        public void ActiveHostChanged_NoExtensions_NoEndpoints()
        {
            Mock<IScriptHostManager> manager = new();

            ExtensionsCompositeEndpointDataSource dataSource = new(manager.Object, _dataSourceLogger);

            IChangeToken token = dataSource.GetChangeToken();
            Assert.False(token.HasChanged);
            manager.Raise(x => x.ActiveHostChanged += null, new ActiveHostChangedEventArgs(null, GetHost()));
            Assert.True(token.HasChanged);
            Assert.Empty(dataSource.Endpoints);
        }

        [Fact]
        public void ActiveHostChanged_NewExtensions_NewEndpoints()
        {
            Mock<IScriptHostManager> manager = new();
            ExtensionsCompositeEndpointDataSource dataSource = new(manager.Object, _dataSourceLogger);
            IHost host = GetHost(new TestEndpoints(new Endpoint(null, null, "Test1"), new Endpoint(null, null, "Test2")));

            IChangeToken token = dataSource.GetChangeToken();
            Assert.False(token.HasChanged);
            manager.Raise(x => x.ActiveHostChanged += null, new ActiveHostChangedEventArgs(null, host));
            Assert.True(token.HasChanged);
            Assert.Collection(
                dataSource.Endpoints,
                endpoint => Assert.Equal("Test1", endpoint.DisplayName),
                endpoint => Assert.Equal("Test2", endpoint.DisplayName));
        }

        [Fact]
        public async Task ActiveHostChanged_MiddlewareWaits_Success()
        {
            Mock<IScriptHostManager> manager = new();

            ExtensionsCompositeEndpointDataSource dataSource = new(manager.Object, _dataSourceLogger);
            ExtensionsCompositeEndpointDataSource.EnsureInitializedMiddleware middleware =
                new(dataSource, _middlewareLogger) { Timeout = Timeout.InfiniteTimeSpan };
            TestDelegate next = new();

            Task waiter = middleware.InvokeAsync(null, next.InvokeAsync);
            Assert.False(waiter.IsCompleted); // should be blocked until we raise the event.

            manager.Raise(x => x.ActiveHostChanged += null, new ActiveHostChangedEventArgs(null, GetHost()));
            await waiter.WaitAsync(TimeSpan.FromSeconds(5));
            await middleware.Initialized;
            await next.Invoked;
        }

        [Fact]
        public async Task NoActiveHostChanged_MiddlewareWaits_Timeout()
        {
            Mock<IScriptHostManager> manager = new();

            ExtensionsCompositeEndpointDataSource dataSource = new(manager.Object, _dataSourceLogger);
            ExtensionsCompositeEndpointDataSource.EnsureInitializedMiddleware middleware =
                new(dataSource, _middlewareLogger) { Timeout = TimeSpan.Zero };
            TestDelegate next = new();

            await middleware.InvokeAsync(null, next.InvokeAsync).WaitAsync(TimeSpan.FromSeconds(5)); // should not throw
            await Assert.ThrowsAsync<TimeoutException>(() => middleware.Initialized);
            await next.Invoked;

            // invoke again to verify it processes the next request.
            next = new();
            await middleware.InvokeAsync(null, next.InvokeAsync);
            await next.Invoked;
        }

        [Fact]
        public void Dispose_GetThrows()
        {
            Mock<IScriptHostManager> manager = new();
            ExtensionsCompositeEndpointDataSource dataSource = new(manager.Object, _dataSourceLogger);
            IHost host = GetHost(new TestEndpoints(new Endpoint(null, null, "Test1"), new Endpoint(null, null, "Test2")));
            manager.Raise(x => x.ActiveHostChanged += null, new ActiveHostChangedEventArgs(null, host));
            dataSource.Dispose();

            Assert.Throws<ObjectDisposedException>(() => dataSource.Endpoints);
            Assert.Throws<ObjectDisposedException>(() => dataSource.GetChangeToken());

            dataSource.Dispose(); // verify multiple calls works.
        }

        private static IHost GetHost(params WebJobsRpcEndpointDataSource[] extensions)
        {
            ServiceCollection services = new();
            foreach (WebJobsRpcEndpointDataSource endpoint in extensions)
            {
                services.AddSingleton(endpoint);
            }

            IServiceProvider sp = services.BuildServiceProvider();
            return Mock.Of<IHost>(x => x.Services == sp);
        }

        private class TestEndpoints : WebJobsRpcEndpointDataSource
        {
            public TestEndpoints(params Endpoint[] endpoints)
            {
                Endpoints = endpoints;
            }

            public override IReadOnlyList<Endpoint> Endpoints { get; }

            public override IChangeToken GetChangeToken() => NullChangeToken.Singleton;
        }

        private class TestDelegate
        {
            private readonly TaskCompletionSource _invoked = new();

            public Task Invoked => _invoked.Task;

            public Task InvokeAsync(HttpContext context)
            {
                _invoked.TrySetResult();
                return Task.CompletedTask;
            }
        }
    }
}
