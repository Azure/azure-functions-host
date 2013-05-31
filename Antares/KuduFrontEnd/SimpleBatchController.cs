using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Web.Http;
using System.Web.Http.Controllers;
using DaasEndpoints;
using Executor;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using Orchestrator;
using RunnerInterfaces;
using SimpleBatch;
using TriggerService;
using TriggerType = TriggerService.TriggerType;

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
        class FunctionStore : IFunctionTableLookup
        {
            static object _lock = new object();
            static FunctionStore _instance;
            public static FunctionStore Instance(string url)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new FunctionStore(url);
                    }
                }
                return _instance;
            }

            IndexInMemory _store;

            FunctionStore(string url)
            {
                string accountConnectionString = GetAccountString();

                _store = new IndexInMemory();
                var indexer = new Indexer(_store);

                foreach (Assembly a in GetUserAssemblies())
                {
                    indexer.IndexAssembly(m => OnApplyLocationInfo(accountConnectionString, m), a);
                }
            }

            FunctionLocation OnApplyLocationInfo(string accountConnectionString, MethodInfo method)
            {
                return new MethodInfoFunctionLocation
                {                    
                    AccountConnectionString = accountConnectionString,
                    MethodInfo = method
                };
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

        private FunctionStore GetFunctionStore()
        {
            string url = this.Request.RequestUri.ToString();
            var ft = FunctionStore.Instance(url);
            return ft;
        }

        // Index through user functions 
        public AddTriggerPayload Get()
        {
            string url = this.Request.RequestUri.ToString();

            var ft = GetFunctionStore();
            FunctionDefinition[] funcs = ft.ReadAll();

            var triggers = new TriggerRaw[funcs.Length];
            for(int i = 0; i < funcs.Length; i++)
            {
                var func = funcs[i];
                var trigger = CalculateTriggers.GetTriggerRaw(func);
            
                var funcId = func.Location.ToString();
                trigger.CallbackPath = string.Format("{0}?trigger={1}&func={2}", url, trigger.Type, funcId);

                triggers[i] = trigger;
            }
            
            string accountConnectionString = GetAccountString();

            var payload = new AddTriggerPayload
            {
                Credentials = new Credentials
                {
                     AccountConnectionString = accountConnectionString
                },
                Triggers = triggers
            };

            return payload;
        }

        // Azure storage account that all user bindings get resolved relative to. 
        static string GetAccountString()
        {
            string accountConnectionString = ConfigurationManager.AppSettings["SimpleBatchAccountConnectionString"];
            return accountConnectionString;
        }

        static CloudStorageAccount GetAccount()
        {
            string accountConnectionString = GetAccountString();
            return CloudStorageAccount.Parse(accountConnectionString);
        }

        static IEnumerable<Assembly> GetUserAssemblies()
        {
            return AppDomain.CurrentDomain.GetAssemblies();
        }

        public void Post(string trigger, string func)
        {
            TriggerType type;
            if (!Enum.TryParse<TriggerType>(trigger, out type))
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }

            var ft = GetFunctionStore();
            FunctionDefinition x = ft.Lookup(func);
            if (x == null)
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }

            Post(type, x);
        }

        private void Post(TriggerType type, FunctionDefinition func)
        {
            FunctionInvokeRequest instance;

            switch (type)
            {
                case TriggerType.Blob:
                    string blobInputPath = this.Request.Content.ReadAsStringAsync().Result.Trim();
                    var path = new CloudBlobPath(blobInputPath);
                    var account = GetAccount();
                    CloudBlob blob = path.Resolve(account);

                    instance = Worker.GetFunctionInvocation(func, blob);
                    break;

                case TriggerType.Timer:
                    instance = Worker.GetFunctionInvocation(func);
                    break;

                case TriggerType.Queue:
                    byte[] contents = this.Request.Content.ReadAsByteArrayAsync().Result;
                    CloudQueueMessage msg = new CloudQueueMessage(contents); // $$$ Set other properties (like Id)?

                    instance = Worker.GetFunctionInvocation(func, msg);
                    break;                    

                default:
                    // Unrecognized 
                    return;
            }


            StringWriter sw = new StringWriter();
            Console.SetOut(sw);

            IConfiguration config = RunnerHost.Program.InitBinders();
            // $$$ Missing ICall Binder. Should this be using LocalExecutionContext?

            RunnerHost.Program.Invoke(instance, config); 
        }
    }
}
