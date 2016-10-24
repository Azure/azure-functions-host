// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;

namespace Microsoft.Azure.WebJobs.Logging.FunctionalTests
{
    public class TimeBucketTests
    {
        static CloudTable GetTableForName(string tableName)
        {
            // Ctor won't validate, so we just need a well formed URL and it doesn't need to be a real table. 
            var table = new CloudTable(new Uri("http://storage.table.core.windows.net/" + tableName));
            return table;
        }

        [Theory]
        [InlineData("abc", 1, "abc")]
        [InlineData("abc-def", 1, "abc")]
        [InlineData("abc-def", 2, "def")]
        [InlineData("abc-def-xyz", 2, "def")]
        [InlineData("abc-def-xyz", 3, "xyz")]
        public void Parse(string rowKey, int termNumber, string expected)
        {
            string actual = TableScheme.GetNthTerm(rowKey, termNumber);
            Assert.Equal(expected, actual);
        }
        
        [Theory]
        [InlineData("test201204", 201204)]
        [InlineData("test200912", 200912)]
        [InlineData("testcommon", -1)]
        public void GetEpochNumberFromTable(string tableName, long expectedValue)
        {
            var table = GetTableForName(tableName);

            var actual = TimeBucket.GetEpochNumberFromTable(table);
            Assert.Equal(expectedValue, actual);
        }

        [Fact] 
        public void GetTableForDateTime()
        {
            var provider = new MockProvider();
            var table = TimeBucket.GetTableForDateTime(provider, new DateTime(2017, 8, 5));

            var name = table.Name;
            Assert.Equal("foo201708", name);
        }

        [Fact]
        public void ConvertToBucket()
        {
            // Baseline test. Will catch breaking changes in the formula. 
            var date = new DateTime(2019, 12, 1);
            var bucket = TimeBucket.ConvertToBucket(date);
            var expectedBucket = 10474560;
            Assert.Equal(expectedBucket, bucket);

            var date2 = TimeBucket.ConvertToDateTime(bucket);
            Assert.Equal(date, date2);
        }

        class MockProvider : ILogTableProvider
        {
            public CloudTable GetTable(string suffix)
            {
                return GetTableForName("foo" + suffix);
            }

            public Task<CloudTable[]> ListTablesAsync()
            {
                throw new NotImplementedException();
            }
        }
    }
}