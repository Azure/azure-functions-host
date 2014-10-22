// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Moq;
using Xunit;
using Xunit.Extensions;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Executors
{
    public class DefaultStorageAccountProviderTests
    {
        private readonly IConnectionStringProvider _connectionStringProvider = Mock.Of<IConnectionStringProvider>();
        private readonly IStorageAccountParser _storageAccountParser = Mock.Of<IStorageAccountParser>();
        private readonly IStorageCredentialsValidator _storageCredentialsValidator = Mock.Of<IStorageCredentialsValidator>();

        [Theory]
        [InlineData("Dashboard")]
        [InlineData("Storage")]
        public void GetAccountAsync_WhenReadFromConfig_ReturnsParsedAccount(string connectionStringName)
        {
            string connectionString = "valid-ignore";
            IStorageAccount parsedAccount = Mock.Of<IStorageAccount>();
            SetupConnectionStringProvider(connectionStringName, connectionString);
            SetupStorageAccountParser(connectionStringName, connectionString, parsedAccount);
            SetupStorageCredentialsValidator(parsedAccount);
            IStorageAccountProvider provider = new DefaultStorageAccountProvider(_connectionStringProvider, 
                _storageAccountParser, _storageCredentialsValidator);

            IStorageAccount actualAccount = provider.GetAccountAsync(
                connectionStringName, CancellationToken.None).GetAwaiter().GetResult();

            Assert.Same(parsedAccount, actualAccount);
            Mock.Get(_connectionStringProvider).Verify();
            Mock.Get(_storageAccountParser).Verify();
            Mock.Get(_storageCredentialsValidator).Verify();
        }

        [Theory]
        [InlineData("Dashboard")]
        [InlineData("Storage")]
        public void GetAccountAsync_WhenInvalidConfig_PropagatesParserException(string connectionStringName)
        {
            string connectionString = "invalid-ignore";
            Exception expectedException = new InvalidOperationException();
            SetupConnectionStringProvider(connectionStringName, connectionString);
            SetupStorageAccountParserFail(connectionStringName, connectionString, expectedException);
            IStorageAccountProvider provider = new DefaultStorageAccountProvider(_connectionStringProvider,
                _storageAccountParser, _storageCredentialsValidator);

            Exception actualException = Assert.Throws<InvalidOperationException>(
                () => provider.GetAccountAsync(connectionStringName, CancellationToken.None).GetAwaiter().GetResult());

            Assert.Same(expectedException, actualException);
        }

        [Theory]
        [InlineData("Dashboard")]
        [InlineData("Storage")]
        public void GetAccountAsync_WhenInvalidCredentials_PropagatesValidatorException(string connectionStringName)
        {
            string connectionString = "invalid-ignore";
            IStorageAccount parsedAccount = Mock.Of<IStorageAccount>();
            Exception expectedException = new InvalidOperationException();
            SetupConnectionStringProvider(connectionStringName, connectionString);
            SetupStorageAccountParser(connectionStringName, connectionString, parsedAccount);
            SetupStorageCredentialsValidatorFail(parsedAccount, expectedException);
            IStorageAccountProvider provider = new DefaultStorageAccountProvider(_connectionStringProvider,
                _storageAccountParser, _storageCredentialsValidator);

            Exception actualException = Assert.Throws<InvalidOperationException>(
                () => provider.GetAccountAsync(connectionStringName, CancellationToken.None).GetAwaiter().GetResult());

            Assert.Same(expectedException, actualException);
        }

        [Fact]
        public void GetAccountAsync_WhenDashboardOverridden_ReturnsParsedAccount()
        {
            string connectionString = "valid-ignore";
            IStorageAccount parsedAccount = Mock.Of<IStorageAccount>();
            SetupStorageAccountParser(ConnectionStringNames.Dashboard, connectionString, parsedAccount);
            SetupStorageCredentialsValidator(parsedAccount);
            IStorageAccountProvider provider = new DefaultStorageAccountProvider(_connectionStringProvider,
                _storageAccountParser, _storageCredentialsValidator) { DashboardConnectionString = connectionString };

            IStorageAccount actualAccount = provider.GetAccountAsync(
                ConnectionStringNames.Dashboard, CancellationToken.None).GetAwaiter().GetResult();

            Assert.Same(parsedAccount, actualAccount);
            Mock.Get(_storageAccountParser).Verify();
            Mock.Get(_storageCredentialsValidator).Verify();
        }

        [Fact]
        public void GetAccountAsync_WhenStorageOverridden_ReturnsParsedAccount()
        {
            string connectionString = "valid-ignore";
            IStorageAccount parsedAccount = Mock.Of<IStorageAccount>();
            SetupStorageAccountParser(ConnectionStringNames.Storage, connectionString, parsedAccount);
            SetupStorageCredentialsValidator(parsedAccount);
            IStorageAccountProvider provider = new DefaultStorageAccountProvider(_connectionStringProvider,
                _storageAccountParser, _storageCredentialsValidator) { StorageConnectionString = connectionString };

            IStorageAccount actualAccount = provider.GetAccountAsync(
                ConnectionStringNames.Storage, CancellationToken.None).GetAwaiter().GetResult();

            Assert.Same(parsedAccount, actualAccount);
            Mock.Get(_storageAccountParser).Verify();
            Mock.Get(_storageCredentialsValidator).Verify();
        }

        [Fact]
        public void GetAccountAsync_WhenDashboardOverriddenWithNull_ReturnsNull()
        {
            IStorageAccountProvider provider = new DefaultStorageAccountProvider(_connectionStringProvider,
                _storageAccountParser, _storageCredentialsValidator) { DashboardConnectionString = null };

            IStorageAccount actualAccount = provider.GetAccountAsync(
                ConnectionStringNames.Dashboard, CancellationToken.None).GetAwaiter().GetResult();

            Assert.Null(actualAccount);
        }

        [Fact]
        public void GetAccountAsync_WhenStorageOverriddenWithNull_Throws()
        {
            IStorageAccountProvider provider = new DefaultStorageAccountProvider(_connectionStringProvider,
                _storageAccountParser, _storageCredentialsValidator) { StorageConnectionString = null };

            ExceptionAssert.ThrowsInvalidOperation(() => provider.GetAccountAsync(
                ConnectionStringNames.Storage, CancellationToken.None).GetAwaiter().GetResult(), 
                @"Microsoft Azure WebJobs SDK Storage connection string is missing or empty. The Microsoft Azure Storage account connection string can be set in the following ways:" + 
                Environment.NewLine +
                @"1. Set the connection string named 'AzureWebJobsStorage' in the connectionStrings section of the .config file in the following format <add name=""AzureWebJobsStorage"" connectionString=""DefaultEndpointsProtocol=http|https;AccountName=NAME;AccountKey=KEY"" />, or" + 
                Environment.NewLine +
                @"2. Set the environment variable named 'AzureWebJobsStorage', or" + 
                Environment.NewLine + 
                @"3. Set corresponding property of JobHostConfiguration.");
        }

        private void SetupConnectionStringProvider(string connectionStringName, string connectionString)
        {
            Mock.Get(_connectionStringProvider).Setup(m => m.GetConnectionString(connectionStringName))
                .Returns(connectionString)
                .Verifiable("Must retrieve a connection string from the provider.");
        }

        private void SetupStorageAccountParser(string connectionStringName, string connectionString, IStorageAccount parsedAccount)
        {
            Mock.Get(_storageAccountParser).Setup(m => m.ParseAccount(connectionString, connectionStringName))
                .Returns(parsedAccount)
                .Verifiable("Must create parsed account via the parser.");
        }

        private void SetupStorageAccountParserFail(string connectionStringName, string connectionString, Exception exception)
        {
            Mock.Get(_storageAccountParser).Setup(m => m.ParseAccount(connectionString, connectionStringName))
                .Throws(exception);
        }

        private void SetupStorageCredentialsValidator(IStorageAccount parsedAccount)
        {
            Mock.Get(_storageCredentialsValidator).Setup(m => m.ValidateCredentialsAsync(parsedAccount, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(0))
                .Verifiable("Must call the validator method to validate the parsed account");
        }

        private void SetupStorageCredentialsValidatorFail(IStorageAccount parsedAccount, Exception exception)
        {
            Mock.Get(_storageCredentialsValidator).Setup(m => m.ValidateCredentialsAsync(parsedAccount, It.IsAny<CancellationToken>()))
                .Throws(exception);
        }
    }
}
