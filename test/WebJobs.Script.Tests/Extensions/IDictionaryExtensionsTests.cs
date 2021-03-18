// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ImTools;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class IDictionaryExtensionsTests
    {
        private Dictionary<string, object> _dictionary;

        public IDictionaryExtensionsTests()
        {
            _dictionary = new Dictionary<string, object>
            {
                { "BoolValue", true },
                { "IntValue", 777 },
                { "StringValue", "Mathew" }
            };
        }

        [Fact]
        public void TryGetValue_ReturnsExpectedResult()
        {
            bool boolResult;
            Assert.True(_dictionary.TryGetValue<bool>("BoolValue", out boolResult));
            Assert.Equal(_dictionary["BoolValue"], boolResult);

            string stringValue;
            Assert.False(_dictionary.TryGetValue<string>("BoolValue", out stringValue));
            Assert.Equal(null, stringValue);

            Assert.True(_dictionary.TryGetValue<string>("StringValue", out stringValue));
            Assert.Equal("Mathew", stringValue);

            Assert.False(_dictionary.TryGetValue<string>("DoesNotExist", out stringValue));
            Assert.Equal(null, stringValue);
        }

        [Fact]
        public void TryGetValue_CaseSensitivity()
        {
            var dictionaryWithDuplicates = new Dictionary<string, object>
            {
                { "Foo", 1 },
                { "foo", 2 },
                { "bar", 3 },
                { "Bar", 4 },
                { "BAZ", 5 },
            };

            // ignoreCase = true
            int result;
            Assert.True(dictionaryWithDuplicates.TryGetValue<int>("Foo", out result));
            Assert.Equal(1, result);
            Assert.True(dictionaryWithDuplicates.TryGetValue<int>("FOO", out result, ignoreCase: true));
            Assert.Equal(1, result);
            Assert.True(dictionaryWithDuplicates.TryGetValue<int>("foo", out result, ignoreCase: true));  // best match
            Assert.Equal(2, result);
            Assert.True(dictionaryWithDuplicates.TryGetValue<int>("baz", out result, ignoreCase: true));
            Assert.Equal(5, result);

            // ignoreCase = false (default)
            Assert.True(dictionaryWithDuplicates.TryGetValue<int>("Foo", out result));
            Assert.True(dictionaryWithDuplicates.TryGetValue<int>("foo", out result));
            Assert.False(dictionaryWithDuplicates.TryGetValue<int>("FoO", out result));
            Assert.False(dictionaryWithDuplicates.TryGetValue<int>("baz", out result));
        }

        [Fact]
        public async Task ToDictionaryAsync_ReturnsExpectedResult()
        {
            IEnumerable<Foo> items = new List<Foo>()
            {
                new Foo("name1", "value1"),
                new Foo("name2", "value2")
            };

            IDictionary<string, string> dictionary = await items.ToDictionaryAsync(foo => foo.Name, foo => foo.GetValueAsync());

            Assert.Equal(items.Count(), dictionary.Count);
            Assert.Equal(dictionary["name1"], "value1");
            Assert.Equal(dictionary["name2"], "value2");
        }

        internal class Foo
        {
            public Foo(string name, string value)
            {
                Name = name;
                Value = value;
            }

            public string Name { get; set; }

            public string Value { get; set; }

            internal async Task<string> GetValueAsync()
            {
                await Task.Delay(0);
                return Value;
            }
        }
    }
}
