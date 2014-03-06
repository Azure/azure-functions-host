using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Web.Mvc;
using Dashboard.ViewModels;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.Jobs.Host.Protocols;
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
                FunctionFullName = func.Location.FullName,
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
            FunctionDefinition function = GetFunction(functionId);

            if (function == null)
            {
                return HttpNotFound();
            }

            var model = CreateRunFunctionViewModel(function, CreateParameters(function), "Run", null);

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Run(string functionId, Guid hostId, FormCollection form)
        {
            FunctionDefinition function = GetFunction(functionId);

            return Invoke(hostId, form, function, TriggerAndOverrideMessageReasons.RunFromDashboard, null);
        }

        public ActionResult Replay(string parentId)
        {
            Guid parent;
            ExecutionInstanceLogEntity parentLog;
            FunctionDefinition function = GetFunctionFromInstance(parentId, out parent, out parentLog);

            if (function == null)
            {
                return HttpNotFound();
            }

            var model = CreateRunFunctionViewModel(function, CreateParameters(function, parentLog), "Replay", parent);

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Replay(string parentId, Guid hostId, FormCollection form)
        {
            Guid parent;
            FunctionDefinition function = GetFunctionFromInstance(parentId, out parent);

            return Invoke(hostId, form, function, TriggerAndOverrideMessageReasons.ReplayFromDashboard, parent);
        }

        private ExecutionInstanceLogEntity CreateLogEntity(FunctionDefinition function, TriggerAndOverrideMessage message)
        {
            FunctionInvokeRequest instance = Worker.CreateInvokeRequest(function, message);
            return new ExecutionInstanceLogEntity
            {
                QueueTime = DateTime.UtcNow,
                FunctionInstance = instance
            };
        }

        private RunFunctionViewModel CreateRunFunctionViewModel(FunctionDefinition function, IEnumerable<FunctionParameterViewModel> parameters, string submitText, Guid? parentId)
        {
            return new RunFunctionViewModel
            {
                HostId = function.HostId,
                FunctionId = function.Location.GetId(),
                Parameters = parameters,
                ParentId = parentId,
                FunctionName = function.Location.GetShortName(),
                FunctionFullName = function.Location.ToString(),
                HostIsNotRunning = !IsHostRunning(function),
                SubmitText = submitText
            };
        }

        private ActionResult Invoke(Guid hostId, FormCollection form, FunctionDefinition function, string reason, Guid? parentId)
        {
            if (function == null)
            {
                return HttpNotFound();
            }

            IDictionary<string, string> arguments = GetArguments(form);

            Guid id = Guid.NewGuid();

            TriggerAndOverrideMessage message = new TriggerAndOverrideMessage
            {
                Id = id,
                FunctionId = function.Location.GetId(),
                Arguments = arguments,
                ParentId = parentId,
                Reason = reason
            };

            ExecutionInstanceLogEntity logEntity = CreateLogEntity(function, message);
            _functionUpdatedLogger.Log(logEntity);

            _invoker.TriggerAndOverride(hostId, message);

            return Redirect("~/#/functions/invocations/"+id);
        }

        private bool IsHostRunning(FunctionDefinition function)
        {
            RunningHost[] heartbeats = _heartbeatTable.ReadAll();
            return DashboardController.HasValidHeartbeat(function, heartbeats);
        }

        private static IEnumerable<FunctionParameterViewModel> CreateParameters(FunctionDefinition function)
        {
            List<FunctionParameterViewModel> parameters = new List<FunctionParameterViewModel>();

            foreach (ParameterStaticBinding binding in function.Flow.Bindings)
            {
                FunctionParameterViewModel parameter = new FunctionParameterViewModel
                {
                    Name = binding.Name,
                    Description = binding.Prompt,
                    Value = binding.DefaultValue
                };
                parameters.Add(parameter);
            }

            return parameters;
        }

        private static IEnumerable<FunctionParameterViewModel> CreateParameters(FunctionDefinition function, ExecutionInstanceLogEntity log)
        {
            List<FunctionParameterViewModel> parameters = new List<FunctionParameterViewModel>();

            int index = 0;

            foreach (ParameterStaticBinding binding in function.Flow.Bindings)
            {
                FunctionParameterViewModel parameter = new FunctionParameterViewModel
                {
                    Name = binding.Name,
                    Description = binding.Prompt,
                    Value = log.FunctionInstance.Args[index].ConvertToInvokeString()
                };
                parameters.Add(parameter);
                index++;
            }

            return parameters;
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

        private FunctionDefinition GetFunction(string functionId)
        {
            return _functionTableLookup.Lookup(functionId);
        }

        private FunctionDefinition GetFunctionFromInstance(string id, out Guid parsed)
        {
            ExecutionInstanceLogEntity ignored;
            return GetFunctionFromInstance(id, out parsed, out ignored);
        }

        private FunctionDefinition GetFunctionFromInstance(string id, out Guid parsed, out ExecutionInstanceLogEntity instanceLog)
        {
            if (!Guid.TryParse(id, out parsed))
            {
                instanceLog = null;
                return null;
            }

            instanceLog = _functionInstanceLookup.Lookup(parsed);

            if (instanceLog == null)
            {
                return null;
            }

            return GetFunction(instanceLog.FunctionInstance.Location.GetId());
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
            model.Invocation = new InvocationLogViewModel(func);
            model.TriggerReason = new TriggerReasonViewModel(func.FunctionInstance.TriggerReason);
            model.IsAborted = model.Invocation.Status == FunctionInstanceStatus.Running && _terminationSignalReader.IsTerminationRequested(func.HostInstanceId);

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
