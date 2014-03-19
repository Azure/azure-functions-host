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

namespace Dashboard.Controllers
{
    [Route(LegacyNonSpaRouteUrl)]
    public class FunctionController : Controller
    {
        internal const string LegacyNonSpaRouteUrl = "function/{action}";

        private readonly Services _services;
        private readonly IFunctionTableLookup _functionTableLookup;
        private readonly IFunctionInstanceLookup _functionInstanceLookup;
        private readonly IFunctionUpdatedLogger _functionUpdatedLogger;
        private readonly IRunningHostTableReader _heartbeatTable;
        private readonly IInvoker _invoker;

        internal FunctionController(
            Services services,
            IFunctionTableLookup functionTableLookup,
            IFunctionInstanceLookup functionInstanceLookup,
            IFunctionUpdatedLogger functionUpdatedLogger,
            IRunningHostTableReader heartbeatTable,
            IInvoker invoker
            )
        {
            _services = services;
            _functionTableLookup = functionTableLookup;
            _functionInstanceLookup = functionInstanceLookup;
            _functionUpdatedLogger = functionUpdatedLogger;
            _heartbeatTable = heartbeatTable;
            _invoker = invoker;
        }

        public ActionResult Run(string functionId)
        {
            if (functionId == null)
            {
                return HttpNotFound();
            }

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
            if (parentId == null)
            {
                return HttpNotFound();
            }

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

            return Redirect("~/#/functions/invocations/" + id);
        }

        private bool IsHostRunning(FunctionDefinition function)
        {
            RunningHost[] heartbeats = _heartbeatTable.ReadAll();
            return HasValidHeartbeat(function, heartbeats);
        }

        internal static bool HasValidHeartbeat(FunctionDefinition func, IEnumerable<RunningHost> heartbeats)
        {
            RunningHost heartbeat = heartbeats.FirstOrDefault(h => h.HostId == func.HostId);
            return RunningHost.IsValidHeartbeat(heartbeat);
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
                TempData["Message.Text"] = "No invocation found for: " + path;
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
                TempData["Message.Text"] = "No invocation found for: " + path;
                TempData["Message.Level"] = "warning";
                return View();
            }

            TempData["Message.Text"] = "Invocation found for: " + path;
            TempData["Message.Level"] = "info";

            return Redirect("~/#/functions/invocations/" + guid);
        }
    }
}
