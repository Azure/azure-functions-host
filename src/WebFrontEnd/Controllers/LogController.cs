using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;
using AzureTables;


using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;


using Microsoft.WindowsAzure.Jobs.Dashboard.Models.Protocol;

namespace Microsoft.WindowsAzure.Jobs.Dashboard.Controllers
{
    // Controller that access the function invocation logging service to view log information.
#if !SITE_EXTENSION
    [Authorize]
#endif
    public class LogController : Controller
    {
        private readonly Services _services;
        private readonly IFunctionTableLookup _functionTableLookup;

        internal LogController(Services services, IFunctionTableLookup functionTableLookup)
        {
            _services = services;
            _functionTableLookup = functionTableLookup;
        }

        private Services GetServices()
        {
            return _services;
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


        // Given a function, view the entire causal chain.  
        public ActionResult ViewChain(ExecutionInstanceLogEntityModel func)
        {
            ICausalityReader reader = GetServices().GetCausalityReader();
            IFunctionInstanceLookup lookup = GetServices().GetFunctionInstanceLookup();

            // Redirect to ancestor?
            var funcHead = func;
            while(true)
            {
                Guid parentGuid = funcHead.FunctionInstance.TriggerReason.ParentGuid;
                if (parentGuid == Guid.Empty)
                {
                    break;
                }
                parentGuid = reader.GetParent(funcHead.FunctionInstance.Id);

                var parentFunc = lookup.Lookup(parentGuid);
                if (parentFunc == null)
                {
                    break;
                }
                funcHead = new ExecutionInstanceLogEntityModel(parentFunc);
            }

            if (funcHead.FunctionInstance.Id != func.FunctionInstance.Id)
            {
                return RedirectToAction("ViewChain", new { func = funcHead.FunctionInstance.Id });
            }

            FunctionChainModel model = new FunctionChainModel
            {
                 Lookup = lookup,
                 Walker = reader
            };

            
            List<ListNode> nodes = new List<ListNode>();
            model.Nodes = nodes;
            Walk(funcHead, nodes, 0, model);


            // Compute stats
            var minStart = DateTime.MaxValue;
            var maxEnd = DateTime.MinValue;
            foreach (var node in nodes)
            {
                var start = node.Func.UnderlyingObject.StartTime;
                var end = node.Func.UnderlyingObject.EndTime;

                if (start.HasValue)
                {
                    if (start.Value < minStart)
                    {
                        minStart = start.Value;
                    }
                }
                if (end.HasValue)
                {
                    if (end.Value > maxEnd)
                    {
                        maxEnd = end.Value;
                    }
                }
            }
            if (minStart < maxEnd)
            {
                model.Duration = maxEnd - minStart;
            }

            return View(model);
        }

        // Walk the chain and flatten it into a list that's easy to render to HTML. 
        void Walk(ExecutionInstanceLogEntityModel current, List<ListNode> list, int depth, FunctionChainModel model)
        {
            depth++;
            list.Add(new ListNode
            {
                Depth = depth,
                Func = current
            });
            foreach (var child in model.Walker.GetChildren(current.FunctionInstance.Id))
            {
                var childFunc = new ExecutionInstanceLogEntityModel(model.Lookup.Lookup(child.ChildGuid));
                Walk(childFunc, list, depth, model);
            }
        }


        //
        // GET: /Log/

        // List Function invocations 
        public ActionResult ListAllInstances()
        {
            var logger = GetServices().GetFunctionInstanceQuery();

            var model = new LogIndexModel();

            int N = 30;
            var query = new FunctionInstanceQueryFilter();
            model.Logs = logger.GetRecent(N, query).Select(e => new ExecutionInstanceLogEntityModel(e)).ToArray();
            model.Description = string.Format("Last {0} executed functions", N);
            model.HasWarning = HasWarning();

            return View("ListFunctionInstances", model);
        }

        private bool HasWarning()
        {
            var heartbeat = GetServices().GetRunningHostTableReader();
            var heartbeats = heartbeat.ReadAll();
            return heartbeats.Any(h => !RunningHost.IsValidHeartbeat(h));
        }

        public ActionResult GetChargebackLog(int N = 200, string account = null)
        {
            // Defer to the WebAPI controller for the real work. 
            var controller = new ControllersWebApi.LogController(GetServices());
            var resp = controller.GetFunctionLog(N, account);
            var content = resp.Content.ReadAsStringAsync().Result;

            // Return the CSV results in a link that will naturally download as an Excel file. 
            // Be sure to set the Content-Disposition header, which FileResultContent does for us. 
            // http://stackoverflow.com/questions/989927/recommended-way-to-create-an-actionresult-with-a-file-extension
            byte[] byteContents = Encoding.UTF8.GetBytes(content);
            return File(byteContents, "text/csv", "chargeback.csv");                       
        }

        // List all invocation of a specific function. 
        public ActionResult ListFunctionInstances(FunctionDefinitionModel func, bool? success = null)
        {
            var logger = GetServices().GetFunctionInstanceQuery();

            var model = new LogIndexModel();
            int N = 30;
            var query = new FunctionInstanceQueryFilter 
            { 
                Location = func.Location.UnderlyingObject,
                Succeeded = success
            };
            model.Logs = logger.GetRecent(N, query).Select(e => new ExecutionInstanceLogEntityModel(e)).ToArray();

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
        public ActionResult FunctionInstance(ExecutionInstanceLogEntityModel func)
        {
            if (func == null)
            {
                return View("Error");
            }

            // Do some analysis to find inputs, outputs, 
            var model = new LogFunctionModel();
            model.Instance = func;

            IFunctionInstanceLookup lookup = GetServices().GetFunctionInstanceLookup();
            model.Lookup = lookup;

            var instance = model.Instance.FunctionInstance;
            var functionModel = _functionTableLookup.Lookup(instance.Location.UnderlyingObject.GetId());
            if (functionModel == null)
            {
                // Function has been removed from the table.                 
                string msg = string.Format("Function {0} has been unloaded from the server. Can't get log information", func.FunctionInstance.Location.UnderlyingObject.GetId());
                this.ModelState.AddModelError("func", msg);
                return View("Error");
            }

            model.Descriptor = new FunctionDefinitionModel(functionModel);
            // Parallel arrays of static descriptor and actual instance info 
            ParameterRuntimeBinding[] args = instance.UnderlyingObject.Args;
            var flows = model.Descriptor.Flow.Bindings;

            model.Parameters = LogAnalysis.GetParamInfo(model.Descriptor.UnderlyingObject);
            LogAnalysis.ApplyRuntimeInfo(args, model.Parameters);
            LogAnalysis.ApplySelfWatchInfo(instance.UnderlyingObject, model.Parameters);
            
    
            ICausalityReader causalityReader = GetServices().GetCausalityReader();

            model.Children = causalityReader.GetChildren(func.FunctionInstance.Id).Select(r => new TriggerReasonModel(r)).ToArray();

            IPrereqManager pt = GetServices().GetPrereqManager();
            var prereqs = pt.EnumeratePrereqs(instance.Id).ToArray();
            model.Prereqs = Array.ConvertAll(prereqs, id => new ExecutionInstanceLogEntityModel(lookup.Lookup(id)));
                
            return View("FunctionInstance", model);
        }

        [HttpPost]
        public ActionResult AbortFunction(FunctionInvokeRequestModel instance)
        {
            GetServices().PostDeleteRequest(instance.UnderlyingObject);

            // Redict so we swithc verbs from Post to Get
            return RedirectToAction("FunctionInstance", new { func = instance.Id });
        }
                
        // How many times each function has been executed
        public ActionResult Summary()
        {
            var services = GetServices();

            var model = new LogSummaryModel();

            var table = services.GetInvokeStatsTable();
            model.Summary = GetTable<FunctionLocationModel, FunctionStatsEntity, FunctionStatsEntityModel>(table,
                rowKey => new FunctionLocationModel(_functionTableLookup.Lookup(rowKey).Location),
                value => new FunctionStatsEntityModel(value)
                ); // $$$ very inefficient

            // Populate queue. 
            model.QueuedInstances = PeekQueuedInstances().Select(r => new FunctionInvokeRequestModel(r)).ToArray();

            model.QueueDepth = services.GetExecutionQueueDepth();

            return View(model);
        }

        // dict key is the row key. Ignores partition key in azure tables.
        private Dictionary<TKey, TValueViewModel> GetTable<TKey, TValue, TValueViewModel>(
            AzureTable table,
            Func<string, TKey> fpGetKeyFromRowKey,
            Func<TValue, TValueViewModel> transformValueToViewModel
            ) where TKey :  class  where TValue : new()
        {
            var dict = new Dictionary<TKey, TValueViewModel>();

            var all = table.Enumerate();
            foreach (var item in all)
            {
                string rowKey = item["RowKey"];

                var x = ObjectBinderHelpers.ConvertDictToObject<TValue>(item);

                try
                {
                    dict[fpGetKeyFromRowKey(rowKey)] = transformValueToViewModel(x);
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
            List<FunctionInvokeRequest> list = new List<FunctionInvokeRequest>();

            var q = services.GetExecutionQueue();
            var msgs = q.PeekMessages(messageCount: 30);
            foreach (var msg in msgs)
            {
                var instance = JsonCustom.DeserializeObject<FunctionInvokeRequest>(msg.AsString);

                list.Add(instance);
            }
            return list.ToArray();
        }

        CloudStorageAccount ResolveName(string accountName)
        {
            IFunctionTableLookup table = GetServices().GetFunctionTable();
            foreach (var func in table.ReadAll())
            {
                var accountConnectionString = func.Location.AccountConnectionString;
                var accountName2 = Utility.GetAccountName(accountConnectionString);
                if (accountName == accountName2) // ### Case sensitive?
                {
                    return Utility.GetAccount(accountConnectionString);
                }
            }
            return null;
        }

        // ### Beware of security concerns. Does user have permisission to see the blob?
        // Which function wrote to this blob last?
        // Which functions read from this blob?
        // View current value. 
        public ActionResult Blob(string accountName, 
            string path = null,
            string container = null, string blob = null)
        {   
            CloudBlobPath p;
            if (path == null)
            {
                p = new CloudBlobPath(container, blob);
            } else 
            {                
                p = new CloudBlobPath(path);
                container = p.ContainerName;
                blob = p.BlobName;
            }                       
            
            CloudStorageAccount account = ResolveName(accountName);
            if (account == null)
            {
                return new ContentResult { Content = string.Format("Can't resolve account name '{0}'", accountName) };
            }

            CloudBlob x = p.Resolve(account);

            if (!BlobClient.DoesBlobExist(x))
            {
                return new ContentResult { Content = string.Format("Blob doesn't exist.") };
            }

            LogBlobModel2 model = new LogBlobModel2();
            model.AccountName = accountName;
            model.ContainerName = container;
            model.BlobName = blob;
            model.LastModifiedTime = BlobClient.GetBlobModifiedUtcTime(x);
            model.Uri = x.Uri;

            IBlobCausalityLogger logger = new BlobCausalityLogger();
            var guid = logger.GetWriter(x);

            IFunctionInstanceLookup lookup = GetServices().GetFunctionInstanceLookup();
            model.LastWriter = new ExecutionInstanceLogEntityModel(lookup.Lookup(guid));

            model.Length = x.Properties.Length;
            model.ContentType = x.Properties.ContentType;

            // Read the first N characters as content. 
            using (var stream = x.OpenRead())
            {
                int N = 100;
                char[] buffer = new char[N];
                using (var tr = new StreamReader(stream))
                {
                    int len = tr.Read(buffer, 0, buffer.Length);
                    model.Content = new string(buffer, 0, len);
                }
            }

            // $$$ Include list of functions that read this blob. 
            // That can be trickier to find, may require searching all logs, which is expensive. 

            return View(model);
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

    public class LogBlobModel2
    {
        public string AccountName { get; set; }
        public string ContainerName { get; set; }
        public string BlobName { get; set; }

        public DateTime? LastModifiedTime { get; set; }
        public long Length { get; set; }
        public string ContentType { get; set; }
        public string Content { get; set; }

        // Full URI to blob. If container is public, then this can be used to view it. 
        public Uri Uri { get; set; }

        public ExecutionInstanceLogEntityModel LastWriter { get; set; }

    }

    public class LogBlobModel
    {
        public string BlobPath { get; set; }

        public DateTime? LastModifiedTime { get; set; }

        // Full URI to blob. If container is public, then this can be used to view it. 
        public Uri Uri { get; set; }

        // $$$ Readers could be single or plural.
        public ExecutionInstanceLogEntityModel[] Readers { get; set; }

        // $$$ What do multiple writers mean? Can we use timestamps?
        // Sort by most recent.
        public ExecutionInstanceLogEntityModel[] Writer { get; set; }
    }

    public class LogIndexModel
    {
        public string Description { get; set; }
        public ExecutionInstanceLogEntityModel[] Logs { get; set; }
        public bool HasWarning { get; set; }
    }

    public class LogSummaryModel
    {
        // key is the FunctionLocation row key. 
        public IDictionary<FunctionLocationModel, FunctionStatsEntityModel> Summary { get; set; }

        // Top N function instances in the queued. Live values from reading the queue,not from the logs.
        public FunctionInvokeRequestModel[] QueuedInstances { get; set; }

        public int? QueueDepth { get; set; }
    }

    public class LogFunctionModel
    {
        public ExecutionInstanceLogEntityModel Instance { get; set; }

        public FunctionDefinitionModel Descriptor { get; set; }

        public ParamModel[] Parameters { get; set; }

        // Children functions that got triggered due to this function. 
        public TriggerReasonModel[] Children { get; set; }

        // non-null list of prereqs. 
        public ExecutionInstanceLogEntityModel[] Prereqs { get; set; }
               
        // For translating Guids to function names
        internal IFunctionInstanceLookup Lookup { get; set; }
    }
}
