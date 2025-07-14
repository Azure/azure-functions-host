// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using AwesomeAssertions;
using Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics.HealthChecks
{
    public class HealthCheckExtensionsTests
    {
        [Fact]
        public void AddWebJobsScriptHealthChecks_ThrowsOnNullBuilder()
        {
            IHealthChecksBuilder builder = null;
            Action act = () => HealthCheckExtensions.AddWebJobsScriptHealthChecks(builder);
            act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
        }

        [Fact]
        public void AddWebHostHealthCheck_ThrowsOnNullBuilder()
        {
            IHealthChecksBuilder builder = null;
            Action act = () => HealthCheckExtensions.AddWebHostHealthCheck(builder);
            act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
        }

        [Fact]
        public void AddScriptHostHealthCheck_ThrowsOnNullBuilder()
        {
            IHealthChecksBuilder builder = null;
            Action act = () => HealthCheckExtensions.AddScriptHostHealthCheck(builder);
            act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
        }

        [Fact]
        public void AddWebJobsScriptHealthChecks_RegistersBothHealthChecks()
        {
            // arrange
            Mock<IHealthChecksBuilder> builder = new(MockBehavior.Strict);
            builder.Setup(b => b.Add(It.IsAny<HealthCheckRegistration>())).Returns(builder.Object);

            // act
            IHealthChecksBuilder returned = builder.Object.AddWebJobsScriptHealthChecks();

            // assert
            returned.Should().BeSameAs(builder.Object);
            builder.Verify(b => b.Add(It.Is<HealthCheckRegistration>(r =>
                r.Name == "az.functions.web_host.lifecycle" &&
                r.Tags.Contains(HealthCheckTags.Liveness) &&
                r.Factory != null)), Times.Once);
            builder.Verify(b => b.Add(It.Is<HealthCheckRegistration>(r =>
                r.Name == "az.functions.script_host.lifecycle" &&
                r.Tags.Contains(HealthCheckTags.Readiness) &&
                r.Factory != null)), Times.Once);
            builder.VerifyNoOtherCalls();
        }

        [Fact]
        public void AddWebHostHealthCheck_RegistersWebHostHealthCheck()
        {
            // arrange
            Mock<IHealthChecksBuilder> builder = new(MockBehavior.Strict);
            builder.Setup(b => b.Add(It.IsAny<HealthCheckRegistration>())).Returns(builder.Object);

            // act
            IHealthChecksBuilder returned = builder.Object.AddWebHostHealthCheck();

            // assert
            returned.Should().BeSameAs(builder.Object);
            builder.Verify(b => b.Add(It.Is<HealthCheckRegistration>(r =>
                r.Name == "az.functions.web_host.lifecycle" &&
                r.Tags.Contains(HealthCheckTags.Liveness) &&
                r.Factory != null)), Times.Once);
            builder.VerifyNoOtherCalls();
        }

        [Fact]
        public void AddScriptHostHealthCheck_RegistersScriptHostHealthCheck()
        {
            // arrange
            Mock<IHealthChecksBuilder> builder = new(MockBehavior.Strict);
            builder.Setup(b => b.Add(It.IsAny<HealthCheckRegistration>())).Returns(builder.Object);

            // act
            IHealthChecksBuilder returned = builder.Object.AddScriptHostHealthCheck();

            // assert
            returned.Should().BeSameAs(builder.Object);
            builder.Verify(b => b.Add(It.Is<HealthCheckRegistration>(r =>
                r.Name == "az.functions.script_host.lifecycle" &&
                r.Tags.Contains(HealthCheckTags.Readiness) &&
                r.Factory != null)), Times.Once);
            builder.VerifyNoOtherCalls();
        }
    }
}
