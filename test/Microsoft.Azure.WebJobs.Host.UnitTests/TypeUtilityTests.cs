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

        [Theory]
        [InlineData(typeof(TestStorageAccount_NoOverride), null)]
        [InlineData(typeof(TestStorageAccount_ClassOverride), "SecondaryStorage")]
        [InlineData(typeof(TestStorageAccount_MethodOverride), "SecondaryStorage")]
        [InlineData(typeof(TestStorageAccount_ParameterOverride_Attribute), "SecondaryStorage")]
        [InlineData(typeof(TestStorageAccount_ParameterOverride_Property), "SecondaryStorage")]
        public static void GetResolvedAttribute_Storage_ReturnsExpectedResult(Type type, string expected)
        {
            var parameterInfo = type.GetMethod("Test").GetParameters()[0];
            var attribute = TypeUtility.GetResolvedAttribute<QueueTriggerAttribute>(parameterInfo);
            Assert.Equal(expected, attribute.Connection);
        }

#if SERVICE_BUS
        [Theory]
        [InlineData(typeof(TestServiceBusAccount_NoOverride), null)]
        [InlineData(typeof(TestServiceBusAccount_ClassOverride), "SecondaryServiceBus")]
        [InlineData(typeof(TestServiceBusAccount_MethodOverride), "SecondaryServiceBus")]
        [InlineData(typeof(TestServiceBusAccount_ParameterOverride_Attribute), "SecondaryServiceBus")]
        [InlineData(typeof(TestServiceBusAccount_ParameterOverride_Property), "SecondaryServiceBus")]
        public static void GetResolvedAttribute_ServiceBus_ReturnsExpectedResult(Type type, string expected)
        {
            var parameterInfo = type.GetMethod("Test").GetParameters()[0];
            var attribute = TypeUtility.GetResolvedAttribute<ServiceBusTriggerAttribute>(parameterInfo);
            Assert.Equal(expected, attribute.Connection);
        }
#endif
        public static void VoidMethod() { }
        public static async void AsyncVoidMethod() { await Task.FromResult(0); }
        public static async Task AsyncTaskMethod() { await Task.FromResult(0); }

        public class TestStorageAccount_NoOverride
        {
            public static void Test([QueueTrigger("test")] string message) { }
        }

#region StorageAccount
                [StorageAccount("SecondaryStorage")]
                public class TestStorageAccount_ClassOverride
                {
#if SERVICE_BUS
            [ServiceBusAccount("SecondaryServiceBus")]  // expect this to be ignored
#endif
                    public static void Test([QueueTrigger("test")] string message) {}
                }

                public class TestStorageAccount_MethodOverride
                {
                    [StorageAccount("SecondaryStorage")]
                    public static void Test([QueueTrigger("test")] string message) { }
                }

                public class TestStorageAccount_ParameterOverride_Attribute
                {
                    public static void Test([StorageAccount("SecondaryStorage")][QueueTrigger("test")] string message) { }
                }

                public class TestStorageAccount_ParameterOverride_Property
                {
                    public static void Test([QueueTrigger("test", Connection = "SecondaryStorage")] string message) { }
                }
#endregion
#if SERVICE_BUS
#region ServiceBusAccount


        public class TestServiceBusAccount_NoOverride
        {
            public static void Test([ServiceBusTrigger("test")] string message) { }
        }

        [ServiceBusAccount("SecondaryServiceBus")]
        public class TestServiceBusAccount_ClassOverride
        {
            [StorageAccount("SecondaryStorage")]  // expect this to be ignored
            public static void Test([ServiceBusTrigger("test")] string message) { }
        }

        public class TestServiceBusAccount_MethodOverride
        {
            [ServiceBusAccount("SecondaryServiceBus")]
            public static void Test([ServiceBusTrigger("test")] string message) { }
        }

        public class TestServiceBusAccount_ParameterOverride_Attribute
        {
            public static void Test([ServiceBusAccount("SecondaryServiceBus")][ServiceBusTrigger("test")] string message) { }
        }

        public class TestServiceBusAccount_ParameterOverride_Property
        {
            public static void Test([ServiceBusTrigger("test", Connection = "SecondaryServiceBus")] string message) { }
        }
#endregion
#endif
    }
}
