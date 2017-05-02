// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.ApplicationInsights;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Loggers
{
    public class ApplicationInsightsLoggerProviderTests
    {
        [Fact]
        public void Dispose_Disposes()
        {
            var mockTelemetryClientFactory = new Mock<ITelemetryClientFactory>(MockBehavior.Strict);
            mockTelemetryClientFactory.Setup(p => p.Create())
                .Returns<TelemetryClient>(null);
            mockTelemetryClientFactory.Setup(p => p.Dispose());

            var provider = new ApplicationInsightsLoggerProvider(null, mockTelemetryClientFactory.Object);

            provider.Dispose();
            provider.Dispose();

            // Ensure the second call is a no-op
            mockTelemetryClientFactory.Verify(p => p.Dispose(), Times.Once);
        }
    }
}
