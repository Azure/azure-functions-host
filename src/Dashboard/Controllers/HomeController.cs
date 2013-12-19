using System;
using System.Linq;
using System.Web.Mvc;
using Dashboard.Models.Protocol;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Jobs;

namespace Dashboard.Controllers
{
    public class BadConfigController : Controller
    {
        public ViewResult Index()
        {
            return View("BadConfig");
        }
    }

#if !SITE_EXTENSION
    [Authorize]
#endif
    public class HomeController : Controller
    {
        private readonly Services _services;
        private readonly IFunctionTableLookup _functionTableLookup;
        private readonly IRunningHostTableReader _heartbeatTable;

        internal HomeController(Services services, IFunctionTableLookup functionTableLookup, IRunningHostTableReader heartbeatTable)
        {
            _services = services;
            _functionTableLookup = functionTableLookup;
            _heartbeatTable = heartbeatTable;
        }

        private Services GetServices()
        {
            return _services;
        }

        //
        // GET: /Home/

        // Like the homepage
        public ActionResult Index()
        {
            OverviewModel model = new OverviewModel();

            // Get health
            var services = GetServices();
            model.ExecutionSubstrate = services.GetExecutionSubstrateDescription();
            model.VersionInformation = FunctionInvokeRequest.CurrentSchema.ToString();
            model.QueueDepth = services.GetExecutionQueueDepth();
            model.HealthStatus = new ServiceHealthStatusModel(services.GetHealthStatus());
            model.AccountName = services.Account.Credentials.AccountName;

            return View(model);
        }

        public ActionResult ListAllFunctions()
        {
            var heartbeats = _heartbeatTable.ReadAll();
            var allFunctions = _functionTableLookup.ReadAll().Select(f => new FunctionDefinitionModel(f));
            var model = new FunctionListModel
            {
                Functions = allFunctions.GroupBy(f => GetGroupingKey(f.Location.UnderlyingObject), f => new RunningFunctionDefinitionModel(f, heartbeats)),
            };

            if (model.Functions.Any(g => g.Any(f => !f.HostIsRunning)))
            {
                model.HasWarning = true;
            }

            return View(model);
        }

        private object GetGroupingKey(FunctionLocation loc)
        {
            var remoteLoc = loc as RemoteFunctionLocation;
            if (remoteLoc != null)
            {
                return new CloudBlobDescriptorModel(remoteLoc.GetBlob());
            }
            IUrlFunctionLocation urlLoc = loc as IUrlFunctionLocation;
            if (urlLoc != null)
            {
                return new Uri(urlLoc.InvokeUrl);
            }
            return "other";
        }

        public ActionResult ListAllBinders()
        {
            var binderLookupTable = GetServices().GetBinderTable();

            var x = from kv in binderLookupTable.EnumerateDict()
                    select new BinderListModel.Entry
                        {
                            AccountName = Utility.GetAccountName(kv.Value.AccountConnectionString),
                            TypeName = kv.Key.Item2,
                            Path = new CloudBlobPathModel(kv.Value.Path),
                            EntryPoint = string.Format("{0}!{1}", kv.Value.InitAssembly, kv.Value.InitType)
                        };

            var model = new BinderListModel
            {
                Binders = x.ToArray()
            };

            return View(model);
        }

        // Scan a blobpath for new blobs. This will queue to the execution
        // Useful when there are paths that are not being listened on
        public ActionResult RequestScan()
        {
            var functions = _functionTableLookup.ReadAll().Select(f => new FunctionDefinitionModel(f));
            return View(functions);
        }

        public static CloudStorageAccount GetAccount(string AccountName, string AccountKey)
        {
            // $$$ StorageAccounts are more than just Name,Key.
            // So special case getting the dev store account.
            if (AccountName == "devstoreaccount1")
            {
                return CloudStorageAccount.DevelopmentStorageAccount;
            }
            return new CloudStorageAccount(new StorageCredentialsAccountAndKey(AccountName, AccountKey), false);
        }

        // Scan a container and queue execution items.
        [HttpPost]
        public ActionResult RequestScanSubmit(FunctionDefinitionModel function, string accountname, string accountkey, CloudBlobPathModel containerpath)
        {
            CloudStorageAccount account;
            if (function != null)
            {
                account = function.UnderlyingObject.GetAccount();
            }
            else
            {
                account = GetAccount(accountname, accountkey);
            }
            int count = Helpers.ScanBlobDir(GetServices(), account, containerpath.UnderlyingObject);

            RequestScanSubmitModel model = new RequestScanSubmitModel();
            model.CountScanned = count;
            return View(model);
        }

        public ActionResult RegisterFunc()
        {
            return View();
        }

        // Try to lookup the connection string (including key!) for a given account.
        // This works for accounts that are already registered with the service.
        // Return null if not found.
        internal string TryLookupConnectionString(string accountName)
        {
            // If account key is blank, see if we can look it up
            var funcs = _functionTableLookup.ReadAll();
            foreach (var func in funcs)
            {
                var cred = func.GetAccount().Credentials;

                if (string.Compare(cred.AccountName, accountName, ignoreCase: true) == 0)
                {
                    return func.Location.AccountConnectionString;
                }
            }

            // not found
            return null;
        }

        public ActionResult RegisterFuncSubmit(string AccountName, string AccountKey, string ContainerName)
        {
            // Check for typos upfront.

            string accountConnectionString = null;

            // If account key is blank, see if we can look it up
            if (string.IsNullOrWhiteSpace(AccountKey))
            {
                accountConnectionString = TryLookupConnectionString(AccountName);
            }

            if (accountConnectionString == null)
            {
                accountConnectionString = Utility.GetConnectionString(GetAccount(AccountName, AccountKey));
            }

            return RegisterFuncSubmitworker(new IndexOperation
            {
                UserAccountConnectionString = AccountName,
                Blobpath = ContainerName
            });
        }

        [HttpPost]
        public ActionResult RescanFunction(string accountString, string containerName)
        {
            return RegisterFuncSubmitworker(new IndexOperation
                {
                    UserAccountConnectionString = accountString,
                    Blobpath = containerName
                });
        }

        [HttpPost]
        public ActionResult DeleteFunction(FunctionDefinitionModel func)
        {
            var model = new ExecutionController(GetServices(), _functionTableLookup).RegisterFuncSubmitworker(
                new DeleteOperation
                {
                    FunctionToDelete = func.ToString()
                });

            return View("DeleteFuncSubmit", model);
        }

        private ActionResult RegisterFuncSubmitworker(IndexOperation operation)
        {
            var model = new ExecutionController(GetServices(), _functionTableLookup).RegisterFuncSubmitworker(operation);

            return View("RegisterFuncSubmit", model);
        }
    }
}
