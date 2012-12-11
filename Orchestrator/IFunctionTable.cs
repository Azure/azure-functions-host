using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;

namespace Orchestrator
{
    // Manage the function index. 
    public interface IFunctionTable : IFunctionTableLookup
    {
        void Add(FunctionIndexEntity func);
        void Delete(FunctionIndexEntity func);
    }

    public interface IFunctionTableLookup
    {
        // Function Id is the location. 
        FunctionIndexEntity Lookup(string functionId);
        FunctionIndexEntity[] ReadAll();
    }
    public static class IFunctionTableLookupExtensions
    {
        public static FunctionIndexEntity Lookup(this IFunctionTableLookup lookup, FunctionLocation location)
        {
            string rowKey = FunctionIndexEntity.GetRowKey(location);
            return lookup.Lookup(rowKey);
        }
    }
}