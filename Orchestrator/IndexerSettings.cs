using System;
using System.Data.Services.Client;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;

namespace Orchestrator
{
    // Manage the function index. 
    public interface IIndexerSettings
    {
        void Add(FunctionIndexEntity func);

        void Delete(FunctionIndexEntity func);

        FunctionIndexEntity[] ReadFunctionTable(); 

        void CleanFunctionIndex();
    }  
}