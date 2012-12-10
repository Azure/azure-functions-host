using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using AzureTables;
using DaasEndpoints;
using Executor;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Orchestrator;
using RunnerInterfaces;

namespace WebFrontEnd.Controllers
{
    // Controller that access the function invocation logging service to view log information.
    [Authorize]
    public class LogController : Controller
    {
        private static Services GetServices()
        {
            AzureRoleAccountInfo accountInfo = new AzureRoleAccountInfo();
            return new Services(accountInfo);
        }
                        
        protected override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            if (!this.ModelState.IsValid)
            {
                // If we Redirect, we lsoe ModelState.
                filterContext.Result = View("Error");
                return;
            }
            base.OnActionExecuting(filterContext);
        }
        
        // Directed here when there is an error. 
        // This shouldn't happen with logging unless we get bad parameters or server state is deleted.
        public ActionResult Error()
        {
            return View();
        }

        //
        // GET: /Log/

        // List Function invocations 
        public ActionResult ListAllInstances()
        {
            var logger = GetServices().GetFunctionInvokeQuery();

            var model = new LogIndexModel();

            int N = 100;
            var query = new FunctionInstanceQueryFilter();
            model.Logs = logger.GetRecent(N, query).ToArray();
            model.Description = string.Format("Last {0} executed functions", N);

            return View("ListFunctionInstances", model);
        }

        // List all invocation of a specific function. 
        public ActionResult ListFunctionInstances(FunctionIndexEntity func, bool? success = null)
        {
            var logger = GetServices().GetFunctionInvokeQuery();

            var model = new LogIndexModel();
            int N = 100;
            var query = new FunctionInstanceQueryFilter 
            { 
                Location = func.Location,
                Succeeded = success
            };
            model.Logs = logger.GetRecent(N, query).ToArray();

            if (success.HasValue)
            {
                model.Description = string.Format("Last {0} {1} execution instances of: {2}", 
                    N, 
                    (success.Value ? "successful" : "failed"),
                    func);
            }
            else
            {
                model.Description = string.Format("Last {0} execution instances of: {1}", N, func);
            }

            return View("ListFunctionInstances", model);
        }

        // Lookup a single invocation instance 
        public ActionResult FunctionInstance(ExecutionInstanceLogEntity func)
        {
            if (func == null)
            {
            }

            // Do some analysis to find inputs, outputs, 
            var model = new LogFunctionModel();
            model.Instance = func;

            var instance = model.Instance.FunctionInstance;
            model.Descriptor = GetServices().Lookup(instance.Location);

            // Parallel arrays of static descriptor and actual instance info 
            ParameterRuntimeBinding[] args = instance.Args;
            var flows = model.Descriptor.Flow.Bindings;

            model.Parameters = LogAnalysis.GetParamInfo(model.Descriptor);
            LogAnalysis.ApplyRuntimeInfo(args, model.Parameters);
            LogAnalysis.ApplySelfWatchInfo(instance, model.Parameters);                
                
            return View("FunctionInstance", model);
        }

        [HttpPost]
        public ActionResult AbortFunction(FunctionInvokeRequest instance)
        {
            GetServices().PostDeleteRequest(instance);

            // Redict so we swithc verbs from Post to Get
            return RedirectToAction("FunctionInstance", new { func = instance.Id });
        }
                
        // How many times each function has been executed
        public ActionResult Summary()
        {
            var services = GetServices();

            var model = new LogSummaryModel();

            var table = services.GetInvokeStatsTable();
            model.Summary = GetTable<FunctionLocation, FunctionStatsEntity>(table,
                rowKey => services.Lookup(rowKey).Location); // $$$ very inefficient

            // Populate queue. 
            model.QueuedInstances = PeekQueuedInstances();

            model.QueueDepth = services.GetExecutionQueueDepth();

            return View(model);
        }

        // dict key is the row key. Ignores partition key in azure tables.
        private Dictionary<TKey, TValue> GetTable<TKey, TValue>(
            AzureTable table,
            Func<string, TKey> fpGetKeyFromRowKey
            ) where TKey :  new () where TValue : new()
        {
            var dict = new Dictionary<TKey, TValue>();

            var all = table.Enumerate();
            foreach (var item in all)
            {
                string rowKey = item["RowKey"];

                var x = ObjectBinderHelpers.ConvertDictToObject<TValue>(item);

                try
                {
                    dict[fpGetKeyFromRowKey(rowKey)] = x;
                }
                catch
                { 
                    // If we failed to compute the key, then just skip this entry. Not fatal.
                    // This could happen if there are deleted or missing logs. 
                }
            }
            return dict;
        }

        private FunctionInvokeRequest[] PeekQueuedInstances()
        {
            var services = GetServices();
            List<FunctionInvokeRequest> list = new List<RunnerInterfaces.FunctionInvokeRequest>();

            var q = services.GetExecutionQueue();
            var msgs = q.PeekMessages(messageCount: 30);
            foreach (var msg in msgs)
            {
                var instance = JsonCustom.DeserializeObject<FunctionInvokeRequest>(msg.AsString);

                list.Add(instance);
            }
            return list.ToArray();
        }

        // Which function wrote to this blob last?
        // Which functions read from this blob?
        // View current value. 
        public ActionResult Blob(CloudStorageAccount accountName, CloudBlobPath path)
        {
            throw new NotImplementedException("Viewing blob dependencies not implemented");
#if false
            FunctionInvokeLogger logger = GetServices().GetFunctionInvokeLogger();
            var logs = logger.GetAll();
            
            var desc = new CloudBlobDescriptor
            {
                 AccountConnectionString = Utility.GetConnectionString(accountName),
                 ContainerName = path.ContainerName,
                 BlobName = path.BlobName
            };
            var model = LogAnalysis.Compute(desc, logs);


            CloudBlob blob = path.Resolve(accountName);
            model.LastModifiedTime = Utility.GetBlobModifiedUtcTime(blob);
            model.Uri = blob.Uri;

            return View(model);
#endif
        }    
    }

    public class LogBlobModel
    {
        public string BlobPath { get; set; }

        public DateTime? LastModifiedTime { get; set; }

        // Full URI to blob. If container is public, then this can be used to view it. 
        public Uri Uri { get; set; }

        // $$$ Readers could be single or plural.
        public ExecutionInstanceLogEntity[] Readers { get; set; }

        // $$$ What do multiple writers mean? Can we use timestamps?
        // Sort by most recent.
        public ExecutionInstanceLogEntity[] Writer { get; set; }
    }

    public class LogIndexModel
    {
        public string Description { get; set; }
        public ExecutionInstanceLogEntity[] Logs { get; set; }
    }

    public class LogSummaryModel
    {
        // key is the FunctionLocation row key. 
        public IDictionary<FunctionLocation, FunctionStatsEntity> Summary { get; set; }

        // Top N function instances in the queued. Live values from reading the queue,not from the logs.
        public FunctionInvokeRequest[] QueuedInstances { get; set; }

        public int? QueueDepth { get; set; }
    }

    public class LogFunctionModel
    {
        public ExecutionInstanceLogEntity Instance { get; set; }

        public FunctionIndexEntity Descriptor { get; set; }

        public ParamModel[] Parameters { get; set; }
    }
}
