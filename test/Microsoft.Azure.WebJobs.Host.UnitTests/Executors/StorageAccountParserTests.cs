// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.WindowsAzure.Storage;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Executors
{
    public class StorageAccountParserTests
    {
        [Fact]
        public void TryParseAccount_WithEmpty_Fails()
        {
            string connectionString = string.Empty;
            CloudStorageAccount ignore;

            StorageAccountParseResult result = StorageAccountParser.TryParseAccount(connectionString, out ignore);

            Assert.Equal(StorageAccountParseResult.MissingOrEmptyConnectionStringError, result);
        }

        [Fact]
        public void TryParseAccount_WithNull_Fails()
        {
            string connectionString = null;
            CloudStorageAccount ignore;

            StorageAccountParseResult result = StorageAccountParser.TryParseAccount(connectionString, out ignore);

            Assert.Equal(StorageAccountParseResult.MissingOrEmptyConnectionStringError, result);
        }

        [Fact]
        public void TryParseAccount_WithMalformed_Fails()
        {
            string connectionString = "DefaultEndpointsProtocol=https;AccountName=[NOVALUE];AccountKey=[NOVALUE]";
            CloudStorageAccount ignore;

            StorageAccountParseResult result = StorageAccountParser.TryParseAccount(connectionString, out ignore);

            Assert.Equal(StorageAccountParseResult.MalformedConnectionStringError, result);
        }
    }
}
