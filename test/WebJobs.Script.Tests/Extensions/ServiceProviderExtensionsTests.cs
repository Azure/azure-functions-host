// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class ServiceProviderExtensionsTests
    {
        [Fact]
        public void GetScriptHostServiceOrNull_ReturnsExpectedValue()
        {
            ITestInterface test = new TestClass();
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            var scriptHostServiceProviderMock = scriptHostManagerMock.As<IServiceProvider>();
            scriptHostServiceProviderMock.Setup(p => p.GetService(typeof(ITestInterface))).Returns(() => test);

            var serviceProviderMock = new Mock<IServiceProvider>(MockBehavior.Strict);
            serviceProviderMock.Setup(p => p.GetService(typeof(IScriptHostManager))).Returns(scriptHostManagerMock.Object);

            var result = serviceProviderMock.Object.GetScriptHostServiceOrNull<ITestInterface>();
            Assert.Same(test, result);
        }

        [Fact]
        public void GetScriptHostServiceOrNull_NonScriptHost_ReturnsNull()
        {
            var serviceProviderMock = new Mock<IServiceProvider>(MockBehavior.Strict);
            serviceProviderMock.Setup(p => p.GetService(typeof(IScriptHostManager))).Returns(null);

            var result = serviceProviderMock.Object.GetScriptHostServiceOrNull<ITestInterface>();
            Assert.Null(result);
        }

        [Fact]
        public void GetScriptHostServiceOrNull_ContainerDisposed_ReturnsNull()
        {
            var serviceProviderMock = new Mock<IServiceProvider>(MockBehavior.Strict);
            serviceProviderMock.Setup(p => p.GetService(typeof(IScriptHostManager))).Throws(new ObjectDisposedException("test"));
            var result = serviceProviderMock.Object.GetScriptHostServiceOrNull<ITestInterface>();
            Assert.Null(result);
        }
    }
}
