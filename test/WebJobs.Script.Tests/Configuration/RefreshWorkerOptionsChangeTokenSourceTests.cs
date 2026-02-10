// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public sealed class RefreshWorkerOptionsChangeTokenSourceTests
    {
        [Fact]
        public void GetChangeToken_ReturnsValidToken()
        {
            var changeTokenSource = new RefreshWorkerOptionsChangeTokenSource<object>();

            IChangeToken changeToken = changeTokenSource.GetChangeToken();

            Assert.NotNull(changeToken);
            Assert.False(changeToken.HasChanged);
        }

        [Fact]
        public void TriggerChange_SignalsChange()
        {
            var changeTokenSource = new RefreshWorkerOptionsChangeTokenSource<object>();
            IChangeToken changeToken = changeTokenSource.GetChangeToken();

            changeTokenSource.TriggerChange();

            Assert.True(changeToken.HasChanged);
        }

        [Fact]
        public void TriggerChange_CreatesNewToken()
        {
            var changeTokenSource = new RefreshWorkerOptionsChangeTokenSource<object>();
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
            var changeTokenSource = new RefreshWorkerOptionsChangeTokenSource<object>();

            changeTokenSource.Dispose();

            Assert.Throws<ObjectDisposedException>(() => changeTokenSource.GetChangeToken());
        }
    }
}
