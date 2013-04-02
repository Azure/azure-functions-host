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
        void Add(FunctionDefinition func);
        void Delete(FunctionDefinition func);
    }

    public interface IFunctionTableLookup
    {
        // Function Id is the location.ToString().
        FunctionDefinition Lookup(string functionId);
        FunctionDefinition[] ReadAll();
    }
    public static class IFunctionTableLookupExtensions
    {
        public static FunctionDefinition Lookup(this IFunctionTableLookup lookup, FunctionLocation location)
        {
            string rowKey = location.ToString();
            return lookup.Lookup(rowKey);
        }
    }
}