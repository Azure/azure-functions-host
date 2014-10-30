// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Moq;
using Xunit;
using Xunit.Extensions;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Executors
{
    public class DefaultStorageAccountProviderTests
    {
        [Fact]
        public void ConnectionStringProvider_NoDashboardConnectionString_Throws()
        {
            const string DashboardConnectionEnvironmentVariable = "AzureWebJobsDashboard";
            string previousConnectionString = Environment.GetEnvironmentVariable(DashboardConnectionEnvironmentVariable);

            try
            {
                Environment.SetEnvironmentVariable(DashboardConnectionEnvironmentVariable, null);

                IStorageAccountProvider product = new DefaultStorageAccountProvider
                {
                    StorageConnectionString = new CloudStorageAccount(new StorageCredentials("Test", new byte[0], "key"), true).ToString(exportSecrets: true)
                };

                // Act & Assert
                ExceptionAssert.ThrowsInvalidOperation(() =>
                    product.GetDashboardAccountAsync(CancellationToken.None).GetAwaiter().GetResult(),
                    "Microsoft Azure WebJobs SDK Dashboard connection string is missing or empty. The Microsoft Azure Storage account connection string can be set in the following ways:" + Environment.NewLine +
                    "1. Set the connection string named 'AzureWebJobsDashboard' in the connectionStrings section of the .config file in the following format " +
                    "<add name=\"AzureWebJobsDashboard\" connectionString=\"DefaultEndpointsProtocol=http|https;AccountName=NAME;AccountKey=KEY\" />, or" + Environment.NewLine +
                    "2. Set the environment variable named 'AzureWebJobsDashboard', or" + Environment.NewLine +
                    "3. Set corresponding property of JobHostConfiguration.");
            }
            finally
            {
                Environment.SetEnvironmentVariable(DashboardConnectionEnvironmentVariable, previousConnectionString);
            }
        }

        [Theory]
        [InlineData("Dashboard")]
        [InlineData("Storage")]
        public void GetAccountAsync_WhenReadFromConfig_ReturnsParsedAccount(string connectionStringName)
        {
            string connectionString = "valid-ignore";
            IStorageAccount parsedAccount = Mock.Of<IStorageAccount>();
            
            Mock<IConnectionStringProvider> connectionStringProviderMock = new Mock<IConnectionStringProvider>(
                MockBehavior.Strict);
            connectionStringProviderMock.Setup(p => p.GetConnectionString(connectionStringName))
                                        .Returns(connectionString)
                                        .Verifiable();
            IConnectionStringProvider connectionStringProvider = connectionStringProviderMock.Object;

            Mock<IStorageAccountParser> parserMock = new Mock<IStorageAccountParser>(MockBehavior.Strict);
            parserMock.Setup(p => p.ParseAccount(connectionString, connectionStringName))
                      .Returns(parsedAccount)
                      .Verifiable();
            IStorageAccountParser parser = parserMock.Object;

            Mock<IStorageCredentialsValidator> validatorMock = new Mock<IStorageCredentialsValidator>(
                MockBehavior.Strict);
            validatorMock.Setup(v => v.ValidateCredentialsAsync(parsedAccount, It.IsAny<CancellationToken>()))
                         .Returns(Task.FromResult(0))
                         .Verifiable();
            IStorageCredentialsValidator validator = validatorMock.Object;

            IStorageAccountProvider provider = CreateProductUnderTest(connectionStringProvider, parser, validator);

            IStorageAccount actualAccount = provider.GetAccountAsync(
                connectionStringName, CancellationToken.None).GetAwaiter().GetResult();

            Assert.Same(parsedAccount, actualAccount);
            connectionStringProviderMock.Verify();
            parserMock.Verify();
            validatorMock.Verify();
        }

        [Theory]
        [InlineData("Dashboard")]
        [InlineData("Storage")]
        public void GetAccountAsync_WhenInvalidConfig_PropagatesParserException(string connectionStringName)
        {
            string connectionString = "invalid-ignore";
            Exception expectedException = new InvalidOperationException();
            IConnectionStringProvider connectionStringProvider = CreateConnectionStringProvider(connectionStringName,
                connectionString);
            Mock<IStorageAccountParser> parserMock = new Mock<IStorageAccountParser>(MockBehavior.Strict);
            parserMock.Setup(p => p.ParseAccount(connectionString, connectionStringName))
                .Throws(expectedException);
            IStorageAccountParser parser = parserMock.Object;
            IStorageAccountProvider provider = CreateProductUnderTest(connectionStringProvider, parser);

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
            IConnectionStringProvider connectionStringProvider = CreateConnectionStringProvider(connectionStringName,
                connectionString);
            IStorageAccountParser parser = CreateParser(connectionStringName, connectionString, parsedAccount);
            Mock<IStorageCredentialsValidator> validatorMock = new Mock<IStorageCredentialsValidator>(
                MockBehavior.Strict);
            validatorMock.Setup(v => v.ValidateCredentialsAsync(parsedAccount, It.IsAny<CancellationToken>()))
                .Throws(expectedException);
            IStorageCredentialsValidator validator = validatorMock.Object;
            IStorageAccountProvider provider = CreateProductUnderTest(connectionStringProvider, parser, validator);

            Exception actualException = Assert.Throws<InvalidOperationException>(
                () => provider.GetAccountAsync(connectionStringName, CancellationToken.None).GetAwaiter().GetResult());

            Assert.Same(expectedException, actualException);
        }

        [Fact]
        public void GetAccountAsync_WhenDashboardOverridden_ReturnsParsedAccount()
        {
            IConnectionStringProvider connectionStringProvider = CreateDummyConnectionStringProvider();
            string connectionString = "valid-ignore";
            IStorageAccount parsedAccount = Mock.Of<IStorageAccount>();
            IStorageAccountParser parser = CreateParser(ConnectionStringNames.Dashboard, connectionString,
                parsedAccount);
            IStorageCredentialsValidator validator = CreateValidator(parsedAccount);
            DefaultStorageAccountProvider provider = CreateProductUnderTest(connectionStringProvider, parser,
                validator);
            provider.DashboardConnectionString = connectionString;
            IStorageAccount actualAccount = provider.GetAccountAsync(
                ConnectionStringNames.Dashboard, CancellationToken.None).GetAwaiter().GetResult();

            Assert.Same(parsedAccount, actualAccount);
        }

        [Fact]
        public void GetAccountAsync_WhenStorageOverridden_ReturnsParsedAccount()
        {
            IConnectionStringProvider connectionStringProvider = CreateDummyConnectionStringProvider();
            string connectionString = "valid-ignore";
            IStorageAccount parsedAccount = Mock.Of<IStorageAccount>();
            IStorageAccountParser parser = CreateParser(ConnectionStringNames.Storage, connectionString, parsedAccount);
            IStorageCredentialsValidator validator = CreateValidator(parsedAccount);
            DefaultStorageAccountProvider provider = CreateProductUnderTest(connectionStringProvider, parser,
                validator);
            provider.StorageConnectionString = connectionString;

            IStorageAccount actualAccount = provider.GetAccountAsync(
                ConnectionStringNames.Storage, CancellationToken.None).GetAwaiter().GetResult();

            Assert.Same(parsedAccount, actualAccount);
        }

        [Fact]
        public void GetAccountAsync_WhenDashboardOverriddenWithNull_ReturnsNull()
        {
            DefaultStorageAccountProvider provider = CreateProductUnderTest();
            provider.DashboardConnectionString = null;

            IStorageAccount actualAccount = provider.GetAccountAsync(
                ConnectionStringNames.Dashboard, CancellationToken.None).GetAwaiter().GetResult();

            Assert.Null(actualAccount);
        }

        [Fact]
        public void GetAccountAsync_WhenStorageOverriddenWithNull_Throws()
        {
            DefaultStorageAccountProvider provider = CreateProductUnderTest();
            provider.StorageConnectionString = null;

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

        private static IConnectionStringProvider CreateConnectionStringProvider(string connectionStringName,
            string connectionString)
        {
            Mock<IConnectionStringProvider> mock = new Mock<IConnectionStringProvider>(MockBehavior.Strict);
            mock.Setup(p => p.GetConnectionString(connectionStringName))
                .Returns(connectionString);
            return mock.Object;
        }

        private static IConnectionStringProvider CreateDummyConnectionStringProvider()
        {
            return new Mock<IConnectionStringProvider>(MockBehavior.Strict).Object;
        }

        private static IStorageAccountParser CreateDummyParser()
        {
            return new Mock<IStorageAccountParser>(MockBehavior.Strict).Object;
        }

        private static IStorageCredentialsValidator CreateDummyValidator()
        {
            return new Mock<IStorageCredentialsValidator>(MockBehavior.Strict).Object;
        }

        private static IStorageAccountParser CreateParser(string connectionStringName, string connectionString,
            IStorageAccount parsedAccount)
        {
            Mock<IStorageAccountParser> mock = new Mock<IStorageAccountParser>(MockBehavior.Strict);
            mock.Setup(p => p.ParseAccount(connectionString, connectionStringName))
                .Returns(parsedAccount);
            return mock.Object;
        }

        private static DefaultStorageAccountProvider CreateProductUnderTest()
        {
            return CreateProductUnderTest(CreateDummyConnectionStringProvider(), CreateDummyParser());
        }

        private static DefaultStorageAccountProvider CreateProductUnderTest(
            IConnectionStringProvider ambientConnectionStringProvider, IStorageAccountParser storageAccountParser)
        {
            return CreateProductUnderTest(ambientConnectionStringProvider, storageAccountParser,
                CreateDummyValidator());
        }

        private static DefaultStorageAccountProvider CreateProductUnderTest(
            IConnectionStringProvider ambientConnectionStringProvider, IStorageAccountParser storageAccountParser,
            IStorageCredentialsValidator storageCredentialsValidator)
        {
            return new DefaultStorageAccountProvider(ambientConnectionStringProvider, storageAccountParser,
                storageCredentialsValidator);
        }

        private static IStorageCredentialsValidator CreateValidator(IStorageAccount account)
        {
            Mock<IStorageCredentialsValidator> mock = new Mock<IStorageCredentialsValidator>(MockBehavior.Strict);
            mock.Setup(v => v.ValidateCredentialsAsync(account, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(0));
            return mock.Object;
        }
    }
}
