// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Reflection;
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
            builder.Verify(b => b.Add(IsRegistration<WebHostHealthCheck>(
                HealthCheckNames.WebHostLifeCycle, HealthCheckTags.Liveness)),
                Times.Once);
            builder.Verify(b => b.Add(IsRegistration<ScriptHostHealthCheck>(
                HealthCheckNames.ScriptHostLifeCycle, HealthCheckTags.Readiness)),
                Times.Once);
            builder.VerifyNoOtherCalls();
        }

        [Fact]
        public void AddWebHostHealthCheck_RegistersWebHostHealthCheck()
        {
            // arrange
            Mock<IHealthChecksBuilder> builder = new(MockBehavior.Strict);
            builder.Setup(b => b.Add(It.IsAny<HealthCheckRegistration>())).Returns(builder.Object)
                .Callback((HealthCheckRegistration registration) =>
                {
                    Type r = registration.Factory.GetMethodInfo().ReturnType;
                });

            // act
            IHealthChecksBuilder returned = builder.Object.AddWebHostHealthCheck();

            // assert
            returned.Should().BeSameAs(builder.Object);
            builder.Verify(b => b.Add(IsRegistration<WebHostHealthCheck>(
                HealthCheckNames.WebHostLifeCycle, HealthCheckTags.Liveness)),
                Times.Once);
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
            builder.Verify(b => b.Add(IsRegistration<ScriptHostHealthCheck>(
                HealthCheckNames.ScriptHostLifeCycle, HealthCheckTags.Readiness)),
                Times.Once);
            builder.VerifyNoOtherCalls();
        }

        private static HealthCheckRegistration IsRegistration<T>(string name, string tag)
            where T : IHealthCheck
        {
            static bool IsType(HealthCheckRegistration registration)
            {
                if (registration.Factory is not { } factory)
                {
                    return false;
                }

                return factory.GetMethodInfo().ReturnType == typeof(T);
            }

            return Match.Create<HealthCheckRegistration>(r =>
            {
                return r.Name == name && r.Tags.Contains(tag) && IsType(r);
            });
        }
    }
}
