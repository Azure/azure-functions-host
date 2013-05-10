using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Executor;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using Orchestrator;
using RunnerInterfaces;
using RunnerHost;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Data.Services.Common;
using AzureTables;
using SimpleBatch.Client;
using IndexDriver;
using System.Reflection;
using DaasEndpoints;
using Microsoft.Win32;
using System.Net;
using System.Runtime.InteropServices;

namespace ConsoleApplication1
{
    class Program
    {
        static Guid New()
        {
            return Guid.NewGuid();
        }

        static void Main()
        {

            // Stress test Prereq table.
            IPrereqManager pt;


        }

        class NullLogger : IFunctionUpdatedLogger
        {
            public void Log(ExecutionInstanceLogEntity func)
            {                
            }
        }

        static void TestIndex()
        {
            IFunctionTable x = new FuncTable();
            Indexer i = new Indexer(x);


            Func<MethodInfo, FunctionLocation> funcApplyLocation = method => null;
                    

            string dir = @"C:\CodePlex\azuresimplebatch\Temp\TestApp1\bin\Debug";
            i.IndexLocalDir(funcApplyLocation, dir);
        }

        class FuncTable : IFunctionTable
        {
            public void Add(FunctionDefinition func)
            {
            }

            public void Delete(FunctionDefinition func)
            {
            }

            public FunctionDefinition Lookup(string functionId)
            {
                throw new NotImplementedException();
            }

            public FunctionDefinition[] ReadAll()
            {
                return new FunctionDefinition[0];
            }
        }
    }
}
