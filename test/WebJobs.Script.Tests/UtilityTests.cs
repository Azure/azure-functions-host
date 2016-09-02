// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public interface ITestInterface
    {
    }

    public struct TestStruct
    {
        public string Name { get; set; }
        public string Location { get; set; }
    }

    public class TestPoco
    {
        public string Name { get; set; }
        public string Location { get; set; }
    }

    public class UtilityTests
    {
        [Theory]
        [InlineData(typeof(TestPoco), true)]
        [InlineData(typeof(TestStruct), true)]
        [InlineData(typeof(ITestInterface), false)]
        [InlineData(typeof(Guid), false)]
        [InlineData(typeof(int), false)]
        [InlineData(typeof(string), false)]
        public void IsValidUserType_ReturnsExpectedValue(Type type, bool expected)
        {
            Assert.Equal(expected, Utility.IsValidUserType(type));
        }

        [Theory]
        [InlineData("FooBar", "fooBar")]
        [InlineData("FOOBAR", "fOOBAR")]
        [InlineData("fooBar", "fooBar")]
        [InlineData("foo", "foo")]
        [InlineData("Foo", "foo")]
        [InlineData("FOO", "fOO")]
        [InlineData("f", "f")]
        [InlineData("F", "f")]
        [InlineData(null, null)]
        [InlineData("", "")]
        public void ToLowerFirstCharacter_ReturnsExpectedResult(string input, string expected)
        {
            Assert.Equal(Utility.ToLowerFirstCharacter(input), expected);
        }

        [Fact]
        public void ApplyBindingData_HandlesNestedJsonPayloads()
        {
            string input = "{ 'test': 'testing', 'baz': 123, 'nested': [ { 'nesting': 'yes' } ] }";

            var bindingData = new Dictionary<string, object>
            {
                { "foo", "Value1" },
                { "bar", "Value2" },
                { "baz", "Value3" }
            };

            Utility.ApplyBindingData(input, bindingData);

            Assert.Equal("Value1", bindingData["foo"]);
            Assert.Equal("Value2", bindingData["bar"]);
            Assert.Equal("testing", bindingData["test"]);

            // input data overrides ambient data
            Assert.Equal("123", bindingData["baz"]);
        }

        [Fact]
        public void FlattenException_AggregateException_ReturnsExpectedResult()
        {
            ApplicationException ex1 = new ApplicationException("Incorrectly configured setting 'Foo'");
            ex1.Source = "Acme.CloudSystem";

            // a dupe of the first
            ApplicationException ex2 = new ApplicationException("Incorrectly configured setting 'Foo'");
            ex1.Source = "Acme.CloudSystem";

            AggregateException aex = new AggregateException("One or more errors occurred.", ex1, ex2);

            string formattedResult = Utility.FlattenException(aex);
            Assert.Equal("Acme.CloudSystem: Incorrectly configured setting 'Foo'.", formattedResult);
        }

        [Fact]
        public void FlattenException_SingleException_ReturnsExpectedResult()
        {
            ApplicationException ex = new ApplicationException("Incorrectly configured setting 'Foo'");
            ex.Source = "Acme.CloudSystem";

            string formattedResult = Utility.FlattenException(ex);
            Assert.Equal("Acme.CloudSystem: Incorrectly configured setting 'Foo'.", formattedResult);
        }

        [Fact]
        public void FlattenException_MultipleInnerExceptions_ReturnsExpectedResult()
        {
            ApplicationException ex1 = new ApplicationException("Exception message 1");
            ex1.Source = "Source1";

            ApplicationException ex2 = new ApplicationException("Exception message 2.", ex1);
            ex2.Source = "Source2";

            ApplicationException ex3 = new ApplicationException("Exception message 3", ex2);

            string formattedResult = Utility.FlattenException(ex3);
            Assert.Equal("Exception message 3. Source2: Exception message 2. Source1: Exception message 1.", formattedResult);
        }
    }
}
