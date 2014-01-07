using System;
using System.Configuration;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Jobs;

public partial class AzureConfig
{
    public static CloudStorageAccount GetAccount()
    {
        string accountName = ConfigurationManager.AppSettings["accountName"];
        string accountKey = ConfigurationManager.AppSettings["accountKey"];
        if (String.IsNullOrEmpty(accountName))
        {
            throw new InvalidOperationException("The account name is null or empty. Set it in the app.config.");
        }
        if (String.IsNullOrEmpty(accountKey))
        {
            throw new InvalidOperationException("The account key is null or empty. Set it in the app.config.");
        }
        return new CloudStorageAccount(new StorageCredentialsAccountAndKey(accountName, accountKey), false);
    }

    public static string GetConnectionString()
    {
        return Utility.GetConnectionString(GetAccount());
    }
}
