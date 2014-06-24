using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Indexers;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.IntegrationTests
{
    // Provide in-memory settings that can glue an indexer, orchestrator, and execution.
    // Executes via in-memory MethodInfos without azure. 
    // $$$ Merge with any other IFunctionTable, like IndexInMemory
    internal class LocalFunctionTable : IFunctionTable
    {
        List<FunctionDefinition> _funcs = new List<FunctionDefinition>();
        List<MethodInfo> _mapping = new List<MethodInfo>();

        // account is for binding parameters. 
        public LocalFunctionTable(CloudStorageAccount account)
        {
            this.Account = account;
            this.AccountConnectionString = account.ToString(exportSecrets: true);
        }

        public CloudStorageAccount Account { get; private set; }
        public string AccountConnectionString { get; private set; }

        public MethodInfo GetMethod(string functionShortName)
        {
            foreach (var method in _mapping)
            {
                if (method.Name == functionShortName)
                {
                    return method;
                }
            }
            string msg = string.Format("Can't resolve function '{0}'.", functionShortName);
            throw new InvalidOperationException(msg);
        }

        void IFunctionTable.Add(FunctionDefinition func)
        {
            _funcs.Add(func);
        }

        FunctionDefinition[] IFunctionTableLookup.ReadAll()
        {
            return _funcs.ToArray();
        }

        public FunctionDefinition Lookup(string functionId)
        {
            throw new NotImplementedException();
        }
    }
}
