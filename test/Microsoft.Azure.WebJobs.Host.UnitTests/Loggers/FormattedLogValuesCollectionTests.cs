// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Azure.WebJobs.Logging;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Loggers
{
    public class FormattedLogValuesCollectionTests
    {
        [Fact]
        public void Verify_ToString_AndCollection()
        {
            var additionalPayload = new Dictionary<string, object>()
            {
                ["additional"] = "abc",
                ["payload"] = 123
            };

            var payload = new FormattedLogValuesCollection("This {string} has some {data}",
                new object[] { "xyz", 789 }, new ReadOnlyDictionary<string, object>(additionalPayload));

            // Verify string behavior
            Assert.Equal("This xyz has some 789", payload.ToString());

            // Verify the collection
            var expectedPayload = new Dictionary<string, object>
            {
                ["additional"] = "abc",
                ["payload"] = 123,
                ["string"] = "xyz",
                ["data"] = 789,

                // FormattedLogValues adds this automatically to capture the string template
                ["{OriginalFormat}"] = "This {string} has some {data}"
            };

            ValidateCollection(expectedPayload, payload);
        }

        [Fact]
        public void NoAdditionalPayload()
        {
            var payload = new FormattedLogValuesCollection("This {string} has some {data}",
                new object[] { "xyz", 789 }, null);

            // Verify string behavior
            Assert.Equal("This xyz has some 789", payload.ToString());

            // Verify the collection
            var expectedPayload = new Dictionary<string, object>
            {
                ["string"] = "xyz",
                ["data"] = 789,

                // FormattedLogValues adds this automatically to capture the string template
                ["{OriginalFormat}"] = "This {string} has some {data}"
            };

            ValidateCollection(expectedPayload, payload);
        }

        [Fact]
        public void NoStringPayload()
        {
            var additionalPayload = new Dictionary<string, object>()
            {
                ["additional"] = "abc",
                ["payload"] = 123
            };

            var payload = new FormattedLogValuesCollection("This string has no data",
                null, new ReadOnlyDictionary<string, object>(additionalPayload));

            // Verify string behavior
            Assert.Equal("This string has no data", payload.ToString());

            // Verify the collection
            var expectedPayload = new Dictionary<string, object>
            {
                ["additional"] = "abc",
                ["payload"] = 123,

                // FormattedLogValues adds this automatically to capture the string template
                ["{OriginalFormat}"] = "This string has no data"
            };

            ValidateCollection(expectedPayload, payload);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void NoString(string message)
        {
            var additionalPayload = new Dictionary<string, object>()
            {
                ["additional"] = "abc",
                ["payload"] = 123
            };

            var payload = new FormattedLogValuesCollection(message,
                null, new ReadOnlyDictionary<string, object>(additionalPayload));

            // Verify string behavior
            // FormattedLogValues changes nulls to [null]
            var expectedMessage = message ?? "[null]";
            Assert.Equal(expectedMessage, payload.ToString());

            // Verify the collection
            var expectedPayload = new Dictionary<string, object>
            {
                ["additional"] = "abc",
                ["payload"] = 123,

                // FormattedLogValues adds this automatically to capture the string template
                ["{OriginalFormat}"] = expectedMessage
            };

            ValidateCollection(expectedPayload, payload);
        }

        [Fact]
        public void NullValues_WithCurlyBraces()
        {
            var additionalPayload = new Dictionary<string, object>()
            {
                ["additional"] = "abc",
                ["payload"] = 123
            };

            var message = "{this} {should} {work} {";
            var payload = new FormattedLogValuesCollection(message,
                null, new ReadOnlyDictionary<string, object>(additionalPayload));

            // Verify string behavior                        
            Assert.Equal(message, payload.ToString());

            // Verify the collection
            var expectedPayload = new Dictionary<string, object>
            {
                ["additional"] = "abc",
                ["payload"] = 123,

                // FormattedLogValues adds this automatically to capture the string template
                ["{OriginalFormat}"] = message
            };

            ValidateCollection(expectedPayload, payload);
        }

        private static void ValidateCollection(IDictionary<string, object> expectedPayload,
            IReadOnlyList<KeyValuePair<string, object>> actualPayload)
        {
            Assert.Equal(expectedPayload.Count, actualPayload.Count);
            foreach (var kvp in actualPayload)
            {
                Assert.Equal(expectedPayload[kvp.Key], kvp.Value);
            }
        }
    }
}
