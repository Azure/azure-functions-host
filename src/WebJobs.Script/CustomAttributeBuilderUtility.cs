// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script
{
    internal static class CustomAttributeBuilderUtility
    {
        internal static CustomAttributeBuilder GetRetryCustomAttributeBuilder(RetryOptions functionRetry)
        {
            switch (functionRetry.Strategy)
            {
                case RetryStrategy.FixedDelay:
                    Type fixedDelayRetryType = typeof(FixedDelayRetryAttribute);
                    ConstructorInfo fixedDelayRetryCtorInfo = fixedDelayRetryType.GetConstructor(new[] { typeof(int), typeof(string) });
                    CustomAttributeBuilder fixedDelayRetryBuilder = new CustomAttributeBuilder(
                    fixedDelayRetryCtorInfo,
                    new object[] { functionRetry.MaxRetryCount.Value, functionRetry.DelayInterval.ToString() });
                    return fixedDelayRetryBuilder;
                case RetryStrategy.ExponentialBackoff:
                    Type exponentialBackoffRetryType = typeof(ExponentialBackoffRetryAttribute);
                    ConstructorInfo exponentialBackoffDelayRetryCtorInfo = exponentialBackoffRetryType.GetConstructor(new[] { typeof(int), typeof(string), typeof(string) });
                    CustomAttributeBuilder exponentialBackoffRetryBuilder = new CustomAttributeBuilder(
                    exponentialBackoffDelayRetryCtorInfo,
                    new object[] { functionRetry.MaxRetryCount.Value, functionRetry.MinimumInterval.ToString(), functionRetry.MaximumInterval.ToString() });
                    return exponentialBackoffRetryBuilder;
            }
            return null;
        }

        internal static CustomAttributeBuilder GetTimeoutCustomAttributeBuilder(TimeSpan functionTimeout)
        {
            Type timeoutType = typeof(TimeoutAttribute);
            ConstructorInfo ctorInfo = timeoutType.GetConstructor(new[] { typeof(string) });

            PropertyInfo[] propertyInfos = new[]
            {
                timeoutType.GetProperty("ThrowOnTimeout"),
                timeoutType.GetProperty("TimeoutWhileDebugging")
            };

            // Hard-code these for now. Eventually elevate to config
            object[] propertyValues = new object[]
            {
                true,
                true
            };

            return new CustomAttributeBuilder(
                ctorInfo,
                new object[] { functionTimeout.ToString() },
                propertyInfos,
                propertyValues);
        }
    }
}
