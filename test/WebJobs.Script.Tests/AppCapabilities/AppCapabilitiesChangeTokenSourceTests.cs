// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.AppCapabilities
{
    public sealed class AppCapabilitiesChangeTokenSourceTests
    {
        [Fact]
        public void GetChangeToken_ReturnsValidToken()
        {
            var changeTokenSource = new AppCapabilitiesChangeTokenSource();

            IChangeToken changeToken = changeTokenSource.GetChangeToken();

            Assert.NotNull(changeToken);
            Assert.False(changeToken.HasChanged);
        }

        [Fact]
        public void TriggerChange_SignalsChange()
        {
            var changeTokenSource = new AppCapabilitiesChangeTokenSource();
            IChangeToken changeToken = changeTokenSource.GetChangeToken();

            changeTokenSource.TriggerChange();

            Assert.True(changeToken.HasChanged);
        }

        [Fact]
        public void TriggerChange_CreatesNewToken()
        {
            var changeTokenSource = new AppCapabilitiesChangeTokenSource();
            IChangeToken initialToken = changeTokenSource.GetChangeToken();

            changeTokenSource.TriggerChange();
            IChangeToken newToken = changeTokenSource.GetChangeToken();

            Assert.NotSame(initialToken, newToken);
            Assert.True(initialToken.HasChanged);
            Assert.False(newToken.HasChanged);
        }

        [Fact]
        public void Dispose_DisposesTokenSource()
        {
            var changeTokenSource = new AppCapabilitiesChangeTokenSource();

            changeTokenSource.Dispose();

            Assert.Throws<ObjectDisposedException>(() => changeTokenSource.GetChangeToken());
        }

        [Fact]
        public void TriggerChange_AfterDispose_DoesNotThrow()
        {
            var changeTokenSource = new AppCapabilitiesChangeTokenSource();

            changeTokenSource.Dispose();

            // Should not throw - implementation handles disposed state gracefully
            changeTokenSource.TriggerChange();
        }

        [Fact]
        public void Dispose_MultipleCalls_DoesNotThrow()
        {
            var changeTokenSource = new AppCapabilitiesChangeTokenSource();

            changeTokenSource.Dispose();
            changeTokenSource.Dispose();

            // Multiple dispose calls should be safe
        }
    }
}
