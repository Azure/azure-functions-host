using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Internals
{
    internal class FunctionStore : IFunctionTableLookup
    {
        private IndexInMemory _store;
        private Indexer _indexer;

        // storageConnectionString - the account that the functions will bind against. 
        // Index all methods in the types provided by the locator
        public FunctionStore(CloudStorageAccount storageAccount, string serviceBusConnectionString, IConfiguration config, IEnumerable<Type> types)
        {
            _store = new IndexInMemory();
            _indexer = new Indexer(_store, config.NameResolver, config.CloudBlobStreamBinderTypes, storageAccount,
                serviceBusConnectionString);

            foreach (Type t in types)
            {
                _indexer.IndexType(t);
            }
        }

        public IBindingProvider BindingProvider
        {
            get { return _indexer.BindingProvider; }
        }
        
        public INameResolver NameResolver
        {
            get { return _indexer.NameResolver; }
        }

        public FunctionDefinition Lookup(string functionId)
        {
            IFunctionTableLookup x = _store;
            return x.Lookup(functionId);
        }

        public FunctionDefinition[] ReadAll()
        {
            IFunctionTableLookup x = _store;
            return x.ReadAll();
        }
    }
}
