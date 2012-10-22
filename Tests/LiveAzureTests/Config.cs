using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LiveAzureTests;
using Microsoft.WindowsAzure;
using RunnerInterfaces;

public partial class AzureConfig
{
    public static CloudStorageAccount GetAccount()
    {
        return new CloudStorageAccount(new StorageCredentialsAccountAndKey(accountName, accountKey), false);
    }

    public static string GetConnectionString()
    {
        return Utility.GetConnectionString(GetAccount());
    }
}
