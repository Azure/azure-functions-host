// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class TypeUtilityTests
    {
        [Theory]
        [InlineData(typeof(TypeUtilityTests), false)]
        [InlineData(typeof(string), false)]
        [InlineData(typeof(int), false)]
        [InlineData(typeof(int?), true)]
        [InlineData(typeof(Nullable<int>), true)]
        public void IsNullable_ReturnsExpectedResult(Type type, bool expected)
        {
            Assert.Equal(expected, TypeUtility.IsNullable(type));
        }

        [Theory]
        [InlineData(typeof(TypeUtilityTests), "TypeUtilityTests")]
        [InlineData(typeof(string), "String")]
        [InlineData(typeof(int), "Int32")]
        [InlineData(typeof(int?), "Nullable<Int32>")]
        [InlineData(typeof(Nullable<int>), "Nullable<Int32>")]
        public void GetFriendlyName(Type type, string expected)
        {
            Assert.Equal(expected, TypeUtility.GetFriendlyName(type));
        }
    }
}
