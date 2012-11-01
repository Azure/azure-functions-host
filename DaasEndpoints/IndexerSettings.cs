using System;
using System.Data.Services.Client;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;

namespace Orchestrator
{
    // Write to cloud. 
    public class CloudIndexerSettings : IIndexerSettings
    {
        public CloudStorageAccount Account { get; set; }

        // $$$ Should be changed to use an AzureTable<FunctionIndexEntity>
        // Table name is restrictive, must match: "^[A-Za-z][A-Za-z0-9]{2,62}$"
        public string FunctionIndexTableName { get; set; }

        public virtual void Add(FunctionIndexEntity func)
        {
            // $$$ Batch this (AzureTable would handle that)
            Utility.AddTableRow(Account, FunctionIndexTableName, func);
        }

        public virtual void CleanFunctionIndex()
        {
            Utility.DeleteTable(Account, FunctionIndexTableName);
        }

        public void Delete(FunctionIndexEntity func)
        {
            Utility.DeleteTableRow(Account, FunctionIndexTableName, func);
        }

        public FunctionIndexEntity[] ReadFunctionTable()
        {
            var funcs = Utility.ReadTable<FunctionIndexEntity>(Account, FunctionIndexTableName);
            return funcs;
        }
    }
}