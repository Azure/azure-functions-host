using System;
using System.Linq;
using System.Web.Mvc;
using System.Web.Routing;
using Dashboard.ViewModels;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.StorageClient;
using FunctionInstanceStatus = Dashboard.ViewModels.FunctionInstanceStatus;

namespace Dashboard.Controllers
{
    public class FunctionController : Controller
    {
        private readonly Services _services;
        private readonly IFunctionTableLookup _functionTableLookup;
        private readonly IFunctionInstanceLookup _functionInstanceLookup;
        private readonly IProcessTerminationSignalReader _terminationSignalReader;
        private readonly IProcessTerminationSignalWriter _terminationSignalWriter;

        private const int MaxPageSize = 50;
        private const int DefaultPageSize = 10;

        internal FunctionController(
            Services services,
            IFunctionTableLookup functionTableLookup,
            IFunctionInstanceLookup functionInstanceLookup,
            IProcessTerminationSignalReader terminationSignalReader,
            IProcessTerminationSignalWriter terminationSignalWriter)
        {
            _services = services;
            _functionTableLookup = functionTableLookup;
            _functionInstanceLookup = functionInstanceLookup;
            _terminationSignalReader = terminationSignalReader;
            _terminationSignalWriter = terminationSignalWriter;
        }

        public ActionResult PartialInvocationLog()
        {
            var logger = _services.GetFunctionInstanceQuery();

            var query = new FunctionInstanceQueryFilter();
            var model = logger
                .GetRecent(10, query)
                .Select(x => new InvocationLogViewModel(x))
                .ToArray();

            return PartialView(model);
        }

        public ActionResult FunctionInstances(string functionName, bool? success, int? page, int? pageSize)
        {
            if (String.IsNullOrWhiteSpace(functionName))
            {
                return HttpNotFound();
            }

            FunctionDefinition func = _functionTableLookup.Lookup(functionName);

            if (func == null)
            {
                return HttpNotFound();
            }

            // ensure PageSize is not too big, and define a default value if not provided
            pageSize = pageSize.HasValue ? Math.Min(MaxPageSize, pageSize.Value) : DefaultPageSize;
            pageSize = Math.Max(1, pageSize.Value);

            page = page.HasValue ? page : 1;
            
            var skip = ((page - 1)*pageSize.Value).Value;

            // Do some analysis to find inputs, outputs, 
            var model = new FunctionInstancesViewModel
            {
                FunctionName = functionName,
                Success = success,
                Page = page,
                PageSize = pageSize.Value
            };

            var query = new FunctionInstanceQueryFilter
            {
                Location = func.Location,
                Succeeded = success
            };

            var logger = _services.GetFunctionInstanceQuery();

            // load pageSize + 1 to check if there is another page
            model.InvocationLogViewModels = logger
                .GetRecent(pageSize.Value + 1, skip, query)
                .Select(e => new InvocationLogViewModel(e))
                .ToArray();

            return View(model);
        }

        public ActionResult FunctionInstance(string id)
        {
            if (String.IsNullOrWhiteSpace(id))
            {
                return HttpNotFound();
            }

            Guid guid;
            if (!Guid.TryParse(id, out guid))
            {
                return HttpNotFound();
            }

            var func = _functionInstanceLookup.Lookup(guid);

            if (func == null)
            {
                return HttpNotFound();
            }

            var model = new FunctionInstanceDetailsViewModel();
            model.InvocationLogViewModel = new InvocationLogViewModel(func);
            model.TriggerReason = new TriggerReasonViewModel(func.FunctionInstance.TriggerReason);
            model.IsAborted = model.InvocationLogViewModel.Status == FunctionInstanceStatus.Running && _terminationSignalReader.IsTerminationRequested(func.HostInstanceId);

            // Do some analysis to find inputs, outputs, 

            var functionModel = _functionTableLookup.Lookup(func.FunctionInstance.Location.GetId());

            if (functionModel != null)
            {
                var descriptor = new FunctionDefinitionViewModel(functionModel);

                // Parallel arrays of static descriptor and actual instance info 
                ParameterRuntimeBinding[] args = func.FunctionInstance.Args;

                model.Parameters = LogAnalysis.GetParamInfo(descriptor.UnderlyingObject);
                LogAnalysis.ApplyRuntimeInfo(args, model.Parameters);
                LogAnalysis.ApplySelfWatchInfo(func.FunctionInstance, model.Parameters);
            }

            ICausalityReader causalityReader = _services.GetCausalityReader();

            // fetch direct children
            model.Children = causalityReader
                .GetChildren(func.FunctionInstance.Id)
                .Select(r => new InvocationLogViewModel(_functionInstanceLookup.Lookup(r.ChildGuid))).ToArray();

            // fetch ancestor
            var parentGuid = func.FunctionInstance.TriggerReason.ParentGuid;
            if (parentGuid != Guid.Empty)
            {
                model.Ancestor = new InvocationLogViewModel(_functionInstanceLookup.Lookup(parentGuid));
            }

            return View("FunctionInstance", model);
        }

        public ActionResult LookupBlob(string path)
        {
            if (String.IsNullOrEmpty(path))
            {
                TempData["Message.Text"] = "Blob path can't be empty";
                TempData["Message.Level"] = "danger";
                return RedirectToAction("Index", "Dashboard");
            }

            var p = new CloudBlobPath(path.Trim());

            CloudStorageAccount account = Utility.GetAccount(_services.AccountConnectionString);

            if (account == null)
            {
                TempData["Message.Text"] = "Account not found";
                TempData["Message.Level"] = "danger";
                return RedirectToAction("Index", "Dashboard");
            }

            CloudBlob blob;

            try
            {
                blob = p.Resolve(account);
            }
            catch
            {
                blob = null;
            }

            if (blob == null)
            {
                TempData["Message.Text"] = "No job found for: " + path;
                TempData["Message.Level"] = "warning";
                return RedirectToAction("Index", "Dashboard");
            }

            Guid guid;

            try
            {
                IBlobCausalityLogger logger = new BlobCausalityLogger();
                guid = logger.GetWriter(blob);
            }
            catch
            {
                guid = Guid.Empty;
            } 

            if (guid == Guid.Empty)
            {
                TempData["Message.Text"] = "No job found for: " + path;
                TempData["Message.Level"] = "warning";
                return RedirectToAction("Index", "Dashboard");
            }

            IFunctionInstanceLookup lookup = _services.GetFunctionInstanceLookup();

            TempData["Message.Text"] = "Job found for: " + path;
            TempData["Message.Level"] = "info";
            
            return RedirectToAction("FunctionInstance", new {lookup.Lookup(guid).FunctionInstance.Id});
        }

        [HttpPost]
        public ActionResult Abort(string id)
        {
            if (String.IsNullOrWhiteSpace(id))
            {
                return HttpNotFound();
            }

            Guid guid;
            if (!Guid.TryParse(id, out guid))
            {
                return HttpNotFound();
            }

            var func = _functionInstanceLookup.Lookup(guid);

            if (func == null)
            {
                return HttpNotFound();
            }

            _terminationSignalWriter.RequestTermination(func.HostInstanceId);

            return RedirectToAction("FunctionInstance", new { id });
        }
    }
}
