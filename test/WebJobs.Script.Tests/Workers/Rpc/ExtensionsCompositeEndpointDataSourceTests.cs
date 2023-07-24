// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Rpc.Core.Internal;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class ExtensionsCompositeEndpointDataSourceTests
    {
        [Fact]
        public void NoActiveHost_NoEndpoints()
        {
            ExtensionsCompositeEndpointDataSource dataSource = new(Mock.Of<IScriptHostManager>());
            Assert.Empty(dataSource.Endpoints);
        }

        [Fact]
        public void ActiveHostChanged_NullHost_NoEndpoints()
        {
            Mock<IScriptHostManager> manager = new();
            ExtensionsCompositeEndpointDataSource dataSource = new(manager.Object);

            IChangeToken token = dataSource.GetChangeToken();
            Assert.False(token.HasChanged);
            manager.Raise(x => x.ActiveHostChanged += null, new ActiveHostChangedEventArgs(null, null));
            Assert.True(token.HasChanged);
            Assert.Empty(dataSource.Endpoints);
        }

        [Fact]
        public void ActiveHostChanged_NoExtensions_NoEndpoints()
        {
            Mock<IScriptHostManager> manager = new();
            ExtensionsCompositeEndpointDataSource dataSource = new(manager.Object);

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
            ExtensionsCompositeEndpointDataSource dataSource = new(manager.Object);
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
        public void Dispose_GetThrows()
        {
            Mock<IScriptHostManager> manager = new();
            ExtensionsCompositeEndpointDataSource dataSource = new(manager.Object);
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
    }
}
