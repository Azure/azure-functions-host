// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
using Microsoft.Azure.WebJobs.Script.Description;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionInvokerBaseTests
    {
        [Fact]
        public void GetBindingData_HandlesNestedJsonPayloads()
        {
            string input = "{ 'test': 'testing', 'baz': 123, 'nested': [ { 'nesting': 'yes' } ] }";

            var binderMock = new Mock<Binder>(MockBehavior.Strict);

            var bindingData = new Dictionary<string, object>
            {
                { "foo", "Value1" },
                { "bar", "Value2" },
                { "baz", "Value3" }
            };
            binderMock.SetupGet(p => p.BindingData).Returns(bindingData);

            FunctionInvokerBase.ApplyBindingData(input, binderMock.Object);

            Assert.Equal("Value1", bindingData["foo"]);
            Assert.Equal("Value2", bindingData["bar"]);
            Assert.Equal("testing", bindingData["test"]);

            // input data overrides ambient data
            Assert.Equal("123", bindingData["baz"]);
        }
    }
}
