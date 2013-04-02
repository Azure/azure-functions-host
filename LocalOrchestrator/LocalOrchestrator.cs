using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Executor;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerHost;
using RunnerInterfaces;
using SimpleBatch;
using SimpleBatch.Client;

namespace Orchestrator
{
    public class LocalOrchestrator
    {
        // Build by indexing all methods in type
        public static Worker Build(CloudStorageAccount account, Type typeClass)
        {
            IndexInMemory store = new IndexInMemory(account);
            Indexer i = new Indexer(store);

            i.IndexType(store.OnApplyLocationInfo, typeClass);

            IFunctionTable functionTable = store;
            IQueueFunction executor = store;
            var worker = new Worker(functionTable, executor);
            return worker;        
        }

        // Run the method for the given blob parameter.
        public static void Invoke(CloudStorageAccount account, IConfiguration config, MethodInfo method, IDictionary<string, string> parameters)
        {
            InvokeWorker(account, method, config,
               func =>
               {
                   // No blob input information. 
                   return Worker.GetFunctionInvocation(func, parameters);
               });
        }

        public static void Invoke(CloudStorageAccount account, MethodInfo method, IDictionary<string, string> parameters)
        {
            InvokeWorker(account, method,
               func =>
               {
                   // No blob input information. 
                   return Worker.GetFunctionInvocation(func, parameters);
               });
        }

        // Run the method for the given blob parameter.
        public static void Invoke(CloudStorageAccount account, MethodInfo method)
        {
            InvokeWorker(account, method,
                func =>
                {
                    // No blob input information. 
                    return Worker.GetFunctionInvocation(func);
                });
        }


        public static void InvokeOnBlob(CloudStorageAccount account, IConfiguration config, MethodInfo method, string blobPath)
        {
            InvokeWorker(account, method, config,
                func =>
                {
                    CloudBlob blobInput = new CloudBlobPath(blobPath).Resolve(account);
                    return Worker.GetFunctionInvocation(func, blobInput);
                });
        }

        public static void InvokeOnBlob(CloudStorageAccount account, MethodInfo method, string blobPath)
        {
            InvokeWorker(account, method,
                func =>
                {
                    CloudBlob blobInput = new CloudBlobPath(blobPath).Resolve(account);
                    return Worker.GetFunctionInvocation(func, blobInput);
                });
        }

        // Common call point. 

        private static void InvokeWorker(CloudStorageAccount account, MethodInfo method, IConfiguration config, Func<FunctionDefinition, FunctionInvokeRequest> fpGetInstance)
        {            
            FunctionDefinition func = GetFunction(account, method);
            FunctionInvokeRequest instance = GetInstance(fpGetInstance, func);

            IRuntimeBindingInputs inputs = new RuntimeBindingInputs(instance.Location);
            Program.Invoke(config, method, instance.Id, inputs, instance.Args);
        }

        private static void InvokeWorker(CloudStorageAccount account, MethodInfo method, Func<FunctionDefinition, FunctionInvokeRequest> fpGetInstance)
        {
            FunctionDefinition func = GetFunction(account, method);
            FunctionInvokeRequest instance = GetInstance(fpGetInstance, func);

            var config = ReflectionFunctionInvoker.GetConfiguration(account, method.DeclaringType);
                        
            IRuntimeBindingInputs inputs = new RuntimeBindingInputs(func.Location);            
            Program.Invoke(config, method, instance.Id, inputs, instance.Args);
        }

        private static FunctionInvokeRequest GetInstance(Func<FunctionDefinition, FunctionInvokeRequest> fpGetInstance, FunctionDefinition func)
        {
            var instance = fpGetInstance(func);
            instance.Id = Guid.NewGuid(); // add the function instance id for causality tracking
            return instance;
        }

        // Convert MethodInfo --> FunctionIndexEntity
        private static FunctionDefinition GetFunction(CloudStorageAccount account, MethodInfo method)
        {
            IndexInMemory store = new IndexInMemory(account);
            Indexer i = new Indexer(store);

            i.IndexMethod(store.OnApplyLocationInfo, method);

            IFunctionTable functionTable = store;
            var funcs = functionTable.ReadAll();
            FunctionDefinition func = funcs[0];
            return func;
        }
    }
}
