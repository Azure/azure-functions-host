// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class TypeUtilityTests
    {
        [Theory]
        [InlineData("VoidMethod", false)]
        [InlineData("AsyncVoidMethod", true)]
        [InlineData("AsyncTaskMethod", true)]
        public void IsAsync_ReturnsExpectedResult(string methodName, bool expectedResult)
        {
            var method = typeof(TypeUtilityTests).GetMethod(methodName, BindingFlags.Public|BindingFlags.Static);
            bool result = TypeUtility.IsAsync(method);
            Assert.Equal(result, expectedResult);
        }

        [Theory]
        [InlineData("VoidMethod", false)]
        [InlineData("AsyncVoidMethod", true)]
        [InlineData("AsyncTaskMethod", false)]
        public void IsAsyncVoid_ReturnsExpectedResult(string methodName, bool expectedResult)
        {
            var method = typeof(TypeUtilityTests).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            bool result = TypeUtility.IsAsyncVoid(method);
            Assert.Equal(result, expectedResult);
        }

        [Theory]
        [InlineData(typeof(TypeUtilityTests), false)]
        [InlineData(typeof(string), false)]
        [InlineData(typeof(int), false)]
        [InlineData(typeof(int?), true)]
        public void IsNullable_ReturnsExpectedResult(Type type, bool expected)
        {
            Assert.Equal(expected, TypeUtility.IsNullable(type));
        }

        [Theory]
        [InlineData(typeof(TypeUtilityTests), "TypeUtilityTests")]
        [InlineData(typeof(string), "String")]
        [InlineData(typeof(int), "Int32")]
        [InlineData(typeof(int?), "Nullable<Int32>")]
        public void GetFriendlyName(Type type, string expected)
        {
            Assert.Equal(expected, TypeUtility.GetFriendlyName(type));
        }

        public static void VoidMethod() { }
        public static async void AsyncVoidMethod() { await Task.FromResult(0); }
        public static async Task AsyncTaskMethod() { await Task.FromResult(0); }
    }
}
