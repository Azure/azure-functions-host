// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Scaling.Tests
{
    [Collection("Azure Test Collection")]
    public class AppServiceScaleManagerTests
    {
        [Theory, MemberData(nameof(SupportedData))]
        public void SupportedTests(bool runtimeScaleEnabled, string storageConnectionString, string sku, bool expected)
        {
            AppServiceSettings.RuntimeScalingEnabled = runtimeScaleEnabled;
            AppServiceSettings.StorageConnectionString = storageConnectionString;
            AppServiceSettings.Sku = sku;
            try
            {
                // test
                var actual = AppServiceScaleManager.Enabled;

                // assert
                Assert.Equal(expected, actual);
            }
            finally
            {
                ResetEnvironment();
            }
        }

        public static IEnumerable<object[]> SupportedData
        {
            get
            {
                yield return new object[] { true, string.Empty, "Dynamic", false };
                yield return new object[] { true, "account", null, false };
                yield return new object[] { true, "account", string.Empty, false };
                yield return new object[] { true, "account", "Standard", false };
                yield return new object[] { false, "account", "Dynamic", false };
                yield return new object[] { true, "account", "Dynamic", true };
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void RegisterProviderTests(bool registered)
        {
            AppServiceSettings.RuntimeScalingEnabled = true;
            AppServiceSettings.StorageConnectionString = "ConnectionString";
            AppServiceSettings.Sku = "Dynamic";
            try
            {
                Guid activityId;
                var evt = new AutoResetEvent(false);

                // setup
                var provider = new Mock<IWorkerStatusProvider>();
                provider.Setup(p => p.GetWorkerStatus(It.Is<string>(s => Guid.TryParse(s, out activityId))))
                    .Callback((string s) => evt.Set())
                    .Throws(new NotImplementedException());

                // test
                AppServiceScaleManager.RegisterProvider(registered ? provider.Object : null);

                // assert
                Assert.Equal(registered, evt.WaitOne(registered ? 5000 : 1000));
            }
            finally
            {
                ResetEnvironment();
            }
        }

        private void ResetEnvironment()
        {
            AppServiceSettings.RuntimeScalingEnabled = null;
            AppServiceSettings.StorageConnectionString = null;
            AppServiceSettings.Sku = null;
        }
    }
}