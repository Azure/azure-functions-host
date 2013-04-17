using System.Linq;
using System.Web.Mvc;
using DaasEndpoints;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.ServiceRuntime;
using Orchestrator;
using RunnerInterfaces;

namespace WebFrontEnd.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private static Services GetServices()
        {
            AzureRoleAccountInfo accountInfo = new AzureRoleAccountInfo();
            return new Services(accountInfo);
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
            model.QueueDepth = services.GetExecutionQueueDepth();
            model.HealthStatus = services.GetHealthStatus();
            model.AccountName = services.Account.Credentials.AccountName;

            return View(model);
        }

        public ActionResult ListAllFunctions()
        {
            var allFunctions = GetServices().GetFunctionTable().ReadAll();
            var model = new FunctionListModel
            {
                Functions = allFunctions.GroupBy(f => GetGroupingKey(f.Location))
            };

            return View(model);
        }

        private object GetGroupingKey(FunctionLocation loc)
        {
            var remoteLoc = loc as RemoteFunctionLocation;
            if (remoteLoc != null)
            {
                return remoteLoc.GetBlob();
            }
            IUrlFunctionLocation urlLoc = loc as IUrlFunctionLocation;
            if (urlLoc != null)
            {
                return urlLoc.InvokeUrl;
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
                            Path = kv.Value.Path,
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
            var functions = GetServices().GetFunctionTable().ReadAll();
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
        public ActionResult RequestScanSubmit(FunctionDefinition function, string accountname, string accountkey, CloudBlobPath containerpath)
        {
            CloudStorageAccount account;
            if (function != null)
            {
                account = function.GetAccount();
            }
            else
            {
                account = GetAccount(accountname, accountkey);
            }
            int count = Helpers.ScanBlobDir(GetServices(), account, containerpath);

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
        internal static string TryLookupConnectionString(string accountName)
        {
            // If account key is blank, see if we can look it up
            var funcs = GetServices().GetFunctionTable().ReadAll();
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
        public ActionResult DeleteFunction(FunctionDefinition func)
        {
            var model = ExecutionController.RegisterFuncSubmitworker(
                new DeleteOperation
                {
                    FunctionToDelete = func.ToString()
                });

            return View("DeleteFuncSubmit", model);
        }

        private ActionResult RegisterFuncSubmitworker(IndexOperation operation)
        {
            var model = ExecutionController.RegisterFuncSubmitworker(operation);

            return View("RegisterFuncSubmit", model);
        }

        public ActionResult RegisterKuduFunc()
        {
            return View();
        }

        [HttpPost]
        public ActionResult RegisterKuduFuncSubmit(string url)
        {
            var model = WebFrontEnd.ControllersWebApi.KuduController.IndexWorker(url);
            return View(model);
        }

    }
}