// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Config.Tests
{
    public class ScriptTypeLocatorTests
    {
        [Fact]
        public void GetTypes_ThrowsTimeoutException_WhenSetTypesNotCalled()
        {
            var locator = new ScriptTypeLocator(TimeSpan.FromSeconds(3));

            Assert.Throws<TimeoutException>(() => locator.GetTypes());
        }

        [Fact]
        public void GetTypes_ReturnsTypes_WhenSetTypesCalled()
        {
            var locator = new ScriptTypeLocator();
            var expectedTypes = new[] { typeof(string), typeof(int) };

            Task.Run(() => locator.SetTypes(expectedTypes));
            var types = locator.GetTypes();

            Assert.Equal(expectedTypes, types);
        }

        [Fact]
        public void Dispose_DisposesManualResetEventSlim()
        {
            var locator = new ScriptTypeLocator();

            locator.Dispose();

            Assert.Throws<ObjectDisposedException>(() => locator.GetTypes());
        }
    }
}