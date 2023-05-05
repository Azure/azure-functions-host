// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Management
{
    public class AzureStorageInfoValueTests
    {
        [Theory]
        [InlineData(null, null, null, null, null, null, null, false)]
        [InlineData("", null, "", "", "", "", "", false)]
        [InlineData("AZUREFILESSTORAGE_storageid", AzureStorageType.AzureFiles, "", "", "", "", "", false)]
        [InlineData("AZUREBLOBSTORAGE_storageid", AzureStorageType.AzureBlob, "", "", "", "", "", false)]
        [InlineData("AZUREFILESSTORAGE_storageid", AzureStorageType.AzureFiles, "", "sharename", "accesskey", "mountpath", "", false)]
        [InlineData("AZUREFILESSTORAGE_storageid", AzureStorageType.AzureFiles, "accountname", "sharename", "accesskey", "mountpath", "", true)]
        [InlineData("AZUREFILESSTORAGE_storageid", AzureStorageType.AzureFiles, "accountname", "sharename", "accesskey", "mountpath", "smb", true)]
        [InlineData("AZUREBLOBSTORAGE_storageid", AzureStorageType.AzureBlob, "accountname", "", "accesskey", "mountpath", "", false)]
        [InlineData("AZUREBLOBSTORAGE_storageid", AzureStorageType.AzureBlob, "accountname", "sharename", "accesskey", "mountpath", "", true)]
        [InlineData("AZUREFILESSTORAGE_storageid", AzureStorageType.AzureFiles, "accountname", "sharename", "accesskey", "mountpath", "http", true)]
        public void Builds_AzureStorageInfoValue(string id, AzureStorageType? type, string accountName, string shareName, string accessKey, string mountPath, string protocol, bool isValid)
        {
            var key = id;
            var value = $"{accountName}|{shareName}|{accessKey}|{mountPath}|{protocol}";
            var environmentVariable = new KeyValuePair<string, string>(key, value);
            var azureStorageInfoValue = AzureStorageInfoValue.FromEnvironmentVariable(environmentVariable);
            if (isValid)
            {
                Assert.NotNull(azureStorageInfoValue);
                Assert.Equal("storageid", azureStorageInfoValue.Id);
                Assert.Equal(type, azureStorageInfoValue.Type);
                Assert.Equal(accountName, azureStorageInfoValue.AccountName);
                Assert.Equal(shareName, azureStorageInfoValue.ShareName);
                Assert.Equal(accessKey, azureStorageInfoValue.AccessKey);
                Assert.Equal(mountPath, azureStorageInfoValue.MountPath);
            }
            else
            {
                Assert.Null(azureStorageInfoValue);
            }
        }
    }
}
