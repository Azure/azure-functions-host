using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Web.Mvc;
using System.Web.Routing;
using Dashboard.ViewModels;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.Jobs.Host.Runners;
using Microsoft.WindowsAzure.StorageClient;
using FunctionInstanceStatus = Dashboard.ViewModels.FunctionInstanceStatus;

namespace Dashboard.Controllers
{
    public class FunctionController : Controller
    {
        private readonly Services _services;
        private readonly IFunctionTableLookup _functionTableLookup;
        private readonly IFunctionInstanceLookup _functionInstanceLookup;
        private readonly IFunctionUpdatedLogger _functionUpdatedLogger;
        private readonly IRunningHostTableReader _heartbeatTable;
        private readonly IInvoker _invoker;
        private readonly IProcessTerminationSignalReader _terminationSignalReader;
        private readonly IProcessTerminationSignalWriter _terminationSignalWriter;

        private const int MaxPageSize = 50;
        private const int DefaultPageSize = 10;

        internal FunctionController(
            Services services,
            IFunctionTableLookup functionTableLookup,
            IFunctionInstanceLookup functionInstanceLookup,
            IFunctionUpdatedLogger functionUpdatedLogger,
            IRunningHostTableReader heartbeatTable,
            IInvoker invoker,
            IProcessTerminationSignalReader terminationSignalReader,
            IProcessTerminationSignalWriter terminationSignalWriter
            )
        {
            _services = services;
            _functionTableLookup = functionTableLookup;
            _functionInstanceLookup = functionInstanceLookup;
            _functionUpdatedLogger = functionUpdatedLogger;
            _heartbeatTable = heartbeatTable;
            _invoker = invoker;
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

            var skip = ((page - 1) * pageSize.Value).Value;

            RunningHost[] heartbeats = _heartbeatTable.ReadAll();

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

            model.FunctionId = func.Location.GetId();

            return View(model);
        }

        public ActionResult Run(string functionId)
        {
            RunFunctionViewModel model = CreateRunFunctionModel(functionId);

            if (model == null)
            {
                return HttpNotFound();
            }

            return View(model);
        }

        [HttpPost]
        public ActionResult Run(string functionId, FormCollection form)
        {
            FunctionDefinition function = _functionTableLookup.Lookup(functionId);

            if (function == null)
            {
                return HttpNotFound();
            }

            IDictionary<string, string> arguments = GetArguments(form);

            InvocationMessage message = new InvocationMessage
            {
                 Type = InvocationMessageType.TriggerAndOverride,
                 Id = Guid.NewGuid(),
                 FunctionId = functionId,
                 Arguments = arguments,
            };

            ExecutionInstanceLogEntity logEntity = CreateLogEntity(message);

            _functionUpdatedLogger.Log(logEntity);

            _invoker.Invoke(function.HostId, message);

            return RedirectToAction("FunctionInstance", new { id = message.Id });
        }

        private ExecutionInstanceLogEntity CreateLogEntity(InvocationMessage message)
        {
            FunctionInvokeRequest instance = Worker.CreateInvokeRequest(message, _functionTableLookup);
            return new ExecutionInstanceLogEntity {
                QueueTime = DateTime.UtcNow,
                FunctionInstance = instance
            };
        }

        private RunFunctionViewModel CreateRunFunctionModel(string functionId)
        {
            FunctionDefinition func = _functionTableLookup.Lookup(functionId);

            if (func == null)
            {
                return null;
            }

            RouteValueDictionary actionRouteValues = new RouteValueDictionary(RouteData.Values);
            actionRouteValues.Add("functionId", functionId);

            RunningHost[] heartbeats = _heartbeatTable.ReadAll();
            bool hostIsNotRunning = !DashboardController.HasValidHeartbeat(func, heartbeats);

            List<FunctionParameterViewModel> parameters = new List<FunctionParameterViewModel>();

            foreach (ParameterStaticBinding binding in func.Flow.Bindings)
            {
                FunctionParameterViewModel parameter = new FunctionParameterViewModel
                {
                    Name = binding.Name,
                    Description = binding.Description
                };
                parameters.Add(parameter);
            }

            return new RunFunctionViewModel
            {
                FunctionId = func.Location.GetId(),
                FunctionName = func.Location.GetShortName(),
                HostId = func.HostId,
                HostIsNotRunning = hostIsNotRunning,
                ActionRouteValues = actionRouteValues,
                Parameters = parameters
            };
        }

        private static IDictionary<string, string> GetArguments(NameValueCollection form)
        {
            const string Prefix = "argument-";
            Dictionary<string, string> arguments = new Dictionary<string, string>();

            foreach (string key in form.AllKeys)
            {
                if (key.StartsWith(Prefix))
                {
                    string argumentName = key.Substring(Prefix.Length);
                    string value = form[key];
                    arguments.Add(argumentName, value);
                }
            }

            return arguments;
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
                LogAnalysis.ApplyRuntimeInfo(func.FunctionInstance, args, model.Parameters);
                LogAnalysis.ApplySelfWatchInfo(func.FunctionInstance, model.Parameters);
            }

            ICausalityReader causalityReader = _services.GetCausalityReader();

            // fetch direct children
            model.ChildrenIds = causalityReader
                .GetChildren(func.FunctionInstance.Id)
                .Select(r => r.ChildGuid).ToArray();

            // fetch ancestor
            var parentGuid = func.FunctionInstance.TriggerReason.ParentGuid;
            if (parentGuid != Guid.Empty)
            {
                model.Ancestor = new InvocationLogViewModel(_functionInstanceLookup.Lookup(parentGuid));
            }

            return View("FunctionInstance", model);
        }

        public ActionResult SearchBlob(string path)
        {
            ViewBag.Path = path;

            if (String.IsNullOrEmpty(path))
            {
                return View();
            }

            CloudStorageAccount account = Utility.GetAccount(_services.AccountConnectionString);

            if (account == null)
            {
                TempData["Message.Text"] = "Account not found";
                TempData["Message.Level"] = "danger";
                return View();
            }

            CloudBlob blob;

            try
            {
                var p = new CloudBlobPath(path.Trim());
                blob = p.Resolve(account);
            }
            catch (FormatException e)
            {
                TempData["Message.Text"] = e.Message;
                TempData["Message.Level"] = "danger";
                return View();
            }
            catch
            {
                blob = null;
            }

            if (blob == null)
            {
                TempData["Message.Text"] = "No job found for: " + path;
                TempData["Message.Level"] = "warning";
                return View();
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
                return View();
            }

            IFunctionInstanceLookup lookup = _services.GetFunctionInstanceLookup();

            TempData["Message.Text"] = "Job found for: " + path;
            TempData["Message.Level"] = "info";

            return RedirectToAction("FunctionInstance", new { lookup.Lookup(guid).FunctionInstance.Id });
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

        public ActionResult InvocationsByIds(Guid[] invocationIds)
        {
            var invocations = from id in invocationIds
                let invocation = _functionInstanceLookup.Lookup(id)
                select new InvocationLogViewModel(invocation);
            return PartialView("FunctionInvocationChildren", invocations);
        }
    }
}
