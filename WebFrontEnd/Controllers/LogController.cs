using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using AzureTables;
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
        FunctionInvokeLogger _logger = Services.GetFunctionInvokeLogger();
        
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
            var model = new LogIndexModel();
            model.Logs = _logger.GetRecent(100);
            model.Description = "All executed functions";

            return View("ListFunctionInstances", model);
        }

        // List all invocation of a specific function. 
        public ActionResult ListFunctionInstances(FunctionIndexEntity func)
        {
            var model = new LogIndexModel();
            IEnumerable<ExecutionInstanceLogEntity> logs = _logger.GetRecent(100);
            model.Logs = (from log in logs 
                          where log.FunctionInstance.Location.Equals(func.Location) 
                          select log).ToArray();
            model.Description = string.Format("Execution instances of: {0}", func);

            return View("ListFunctionInstances", model);
        }

        // List all invocation of a specific function. 
        // $$$ Are there queries here? (filter on status, timestamp, etc).
        public ActionResult ListFunctionInstancesFailures(FunctionIndexEntity func)
        {
            var model = new LogIndexModel();
            IEnumerable<ExecutionInstanceLogEntity> logs = _logger.GetRecent(100);
            model.Logs = (from log in logs
                          let instance = log.FunctionInstance
                          where instance.Location.Equals(func.Location) && (log.GetStatus() == FunctionInstanceStatus.CompletedFailed)
                          select log).ToArray();
            model.Description = string.Format("Failed execution instances of: {0}", func);

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
            model.Descriptor = Services.Lookup(instance.Location);

            // Parallel arrays of static descriptor and actual instance info 
            ParameterRuntimeBinding[] args = instance.Args;
            var flows = model.Descriptor.Flow.Bindings;

            model.Parameters = LogAnalysis.GetParamInfo(model.Descriptor);
            LogAnalysis.ApplyRuntimeInfo(args, model.Parameters);
            LogAnalysis.ApplySelfWatchInfo(instance, model.Parameters);                
                
            return View("FunctionInstance", model);
        }

        [HttpPost]
        public ActionResult AbortFunction(FunctionInstance instance)
        {
            Services.PostDeleteRequest(instance);

            // Redict so we swithc verbs from Post to Get
            return RedirectToAction("FunctionInstance", new { func = instance.Id });
        }
                
        // How many times each function has been executed
        public ActionResult Summary()
        {
            var model = new LogSummaryModel();
            
            var table = Services.GetInvokeStatsTable();
            model.Summary = GetTable<FunctionLocation, FunctionStatsEntity>(table,
                rowKey => Services.Lookup(rowKey).Location ); // $$$ very inefficient

            // Populate queue. 
            model.QueuedInstances = PeekQueuedInstances();

            model.QueueDepth = Services.GetExecutionQueueDepth();

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

        private FunctionInstance[] PeekQueuedInstances()
        {
            List<FunctionInstance> list = new List<RunnerInterfaces.FunctionInstance>();

            var q = Services.GetExecutionQueueSettings().GetQueue();
            var msgs = q.PeekMessages(messageCount: 30);
            foreach (var msg in msgs)
            {
                var instance = JsonCustom.DeserializeObject<FunctionInstance>(msg.AsString);

                list.Add(instance);
            }
            return list.ToArray();
        }

        // Which function wrote to this blob last?
        // Which functions read from this blob?
        // View current value. 
        public ActionResult Blob(CloudStorageAccount accountName, CloudBlobPath path)
        {
            var logs = _logger.GetAll();
            
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
        public FunctionInstance[] QueuedInstances { get; set; }

        public int? QueueDepth { get; set; }
    }

    public class LogFunctionModel
    {
        public ExecutionInstanceLogEntity Instance { get; set; }

        public FunctionIndexEntity Descriptor { get; set; }

        public ParamModel[] Parameters { get; set; }
    }
}
