// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Dashboard.Data;
using Xunit;

namespace Dashboard.UnitTests.Data
{
    public class AccountProviderTests
    {
        [Fact]
        [Trait("SecretsRequired", "true")]
        public void GetAccounts_ReturnsExpectedResults()
        {
            // TODO: add a couple env accounts and verify
            Environment.SetEnvironmentVariable("MyStorageAccount", "DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=GTfc0SIUd4Mg6n8wBzCRy29wnx2VVd+90MQKnJtgoCj4rB8LAIQ==");

            var accounts = AccountProvider.GetAccounts();

            Assert.True(accounts.ContainsKey("AzureWebJobsStorage"));
            Assert.True(accounts.ContainsKey("AzureWebJobsDashboard"));

            var account = accounts["MyStorageAccount"];
            Assert.Equal("myaccount", account.Credentials.AccountName);
        }
    }
}
