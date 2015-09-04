// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using System.Reflection;
using Xunit;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests
{
    public class ServiceBusAccountTests
    {
        [Fact]
        public void StorageAccountOverrides()
        {
            // param level
            MethodInfo method = typeof(AccountOverrides).GetMethod("ParamOverride", BindingFlags.NonPublic | BindingFlags.Instance);
            ParameterInfo parameter = method.GetParameters().Single(p => p.Name == "s");
            string connectionName = ServiceBusAccount.GetAccountOverrideOrNull(parameter);
            Assert.Equal("param", connectionName);

            // method level
            method = typeof(AccountOverrides).GetMethod("MethodOverride", BindingFlags.NonPublic | BindingFlags.Instance);
            parameter = method.GetParameters().Single(p => p.Name == "s");
            connectionName = ServiceBusAccount.GetAccountOverrideOrNull(parameter);
            Assert.Equal("method", connectionName);

            method = typeof(AccountOverrides).GetMethod("ClassOverride", BindingFlags.NonPublic | BindingFlags.Instance);
            parameter = method.GetParameters().Single(p => p.Name == "s");
            connectionName = ServiceBusAccount.GetAccountOverrideOrNull(parameter);
            Assert.Equal("class", connectionName);
        }

        [ServiceBusAccount("class")]
        private class AccountOverrides
        {
            [ServiceBusAccount("method")]
            private void ParamOverride([ServiceBusAccount("param")] string s)
            {
            }

            [ServiceBusAccount("method")]
            private void MethodOverride(string s)
            {
            }

            private void ClassOverride(string s)
            {
            }
        }
    }
}
