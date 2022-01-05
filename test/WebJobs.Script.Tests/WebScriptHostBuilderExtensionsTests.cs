// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WebScriptHostBuilderExtensionsTests
    {
        [Fact]
        public void Test()
        {
            var builder = new HostBuilder().ConfigureDefaultTestWebScriptHost(null, null, false, configureRootServices: s =>
                {
                    s.AddSingleton<IEnvironment>(p =>
                    {
                        return new TestEnvironment();
                    });

                    var stateProvider = new Mock<IDebugStateProvider>();

                    stateProvider.Setup(d => d.InDebugMode)
                    .Returns(() => true);

                    s.AddSingleton<IDebugStateProvider>(stateProvider.Object);
                });

            var host = builder.Build();

            var hostingEnvironment = host.Services.GetRequiredService<IHostEnvironment>();

            Assert.False(hostingEnvironment.IsDevelopment());
        }
    }
}