using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Http;
using System.Web.Http.Controllers;
using DaasEndpoints;
using Executor;
using Microsoft.WindowsAzure;
using Newtonsoft.Json;
using Orchestrator;
using RunnerInterfaces;

namespace KuduFrontEnd
{
    // $$$ Also include a "HelpPage", similar to dashboard's invoke pages?
    // provides an HTML springboard of the functions in this deployment?


    class AwesomeConfigAttribute : Attribute, IControllerConfiguration
    {
        public void Initialize(HttpControllerSettings controllerSettings,
                               HttpControllerDescriptor controllerDescriptor)
        {
            // Ensure JSON serializes the right way, especially with polymorphism.
            controllerSettings.Formatters.JsonFormatter.SerializerSettings = JsonCustom.NewSettings();
        }
    }

    [AwesomeConfig]
    public class SimpleBatchIndexerController : ApiController    
    {
        // Index through user functions 
        public FunctionDefinition[] Get()
        {
            string url = this.Request.RequestUri.ToString();

            // Azure storage account that all bindings get resolved relative to. 
            // Get from Web.Config
            // Utility.GetConnectionString(CloudStorageAccount.DevelopmentStorageAccount);
            string accountConnectionString = ConfigurationManager.AppSettings["SimpleBatchAccountConnectionString"]; 
            
            // Can fail if accountConnectionString is not set.
            var ft = new IndexInMemory();
            Indexer i = new Indexer(ft);

            foreach (Assembly a in GetUserAssemblies())
            {
                i.IndexAssembly(m => OnApplyLocationInfo(url, accountConnectionString, m), a);
            }

            var funcs = ft.ReadAll();

            return funcs;
        }

        IEnumerable<Assembly> GetUserAssemblies()
        {
            return AppDomain.CurrentDomain.GetAssemblies();
        }

        FunctionLocation OnApplyLocationInfo(string uri, string accountConnectionString,  MethodInfo method)
        {
            return new KuduFunctionLocation
            {
                Uri = uri,
                AccountConnectionString = accountConnectionString,
                AssemblyQualifiedTypeName = method.DeclaringType.AssemblyQualifiedName,
                MethodName = method.Name
            };          
        }

        public KuduFunctionExecutionResult Post(FunctionInvokeRequest request)
        {
            // !!! Move to background thread, can't block. 
            var loc = (KuduFunctionLocation)request.Location;

            MethodInfoFunctionLocation loc2 = loc.Convert();
            var req2 = request.CloneUpdateLocation(loc2);

            // $$$ Console output is not incremental. 
            // !!! What about concurrent Post requests? Will they steal each other's Console.Out?
            StringWriter sw = new StringWriter();
            Console.SetOut(sw);
            var result = RunnerHost.Program.MainWorker(req2);

            return new KuduFunctionExecutionResult
            {
                 Result = result,
                 ConsoleOutput = sw.ToString()
            };
        }
    }
}
