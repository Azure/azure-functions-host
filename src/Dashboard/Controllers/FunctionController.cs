using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Web.Mvc;
using Dashboard.Data;
using Dashboard.Protocols;
using Dashboard.ViewModels;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Protocols;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Controllers
{
    [Route(LegacyNonSpaRouteUrl)]
    public class FunctionController : Controller
    {
        internal const string LegacyNonSpaRouteUrl = "function/{action}";

        private readonly CloudStorageAccount _account;
        private readonly IFunctionTableLookup _functionTableLookup;
        private readonly IFunctionInstanceLookup _functionInstanceLookup;
        private readonly IFunctionQueuedLogger _functionQueuedLogger;
        private readonly IRunningHostTableReader _heartbeatTable;
        private readonly IInvoker _invoker;

        internal FunctionController(
            CloudStorageAccount account,
            IFunctionTableLookup functionTableLookup,
            IFunctionInstanceLookup functionInstanceLookup,
            IFunctionQueuedLogger functionQueuedLogger,
            IRunningHostTableReader heartbeatTable,
            IInvoker invoker
            )
        {
            _account = account;
            _functionTableLookup = functionTableLookup;
            _functionInstanceLookup = functionInstanceLookup;
            _functionQueuedLogger = functionQueuedLogger;
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
            FunctionInstanceSnapshot parentLog;
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

        private FunctionStartedSnapshot CreateFunctionStartedSnapshot(FunctionDefinition function, TriggerAndOverrideMessage message)
        {
            return new FunctionStartedSnapshot
            {
                FunctionInstanceId = message.Id,
                FunctionId = message.FunctionId,
                FunctionFullName = function.Location.FullName,
                FunctionShortName = function.Location.GetShortName(),
                Arguments = CreateArguments(message.Arguments),
                ParentId = message.ParentId,
                Reason = message.Reason,
                StartTime = DateTimeOffset.UtcNow
            };
        }

        private static IDictionary<string, FunctionArgument> CreateArguments(IDictionary<string, string> arguments)
        {
            IDictionary<string, FunctionArgument> returnValue = new Dictionary<string, FunctionArgument>();

            foreach (KeyValuePair<string, string> argument in arguments)
            {
                returnValue.Add(argument.Key, new FunctionArgument { Value = argument.Value });
            }

            return returnValue;
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

            FunctionStartedSnapshot snapshot = CreateFunctionStartedSnapshot(function, message);
            _functionQueuedLogger.LogFunctionQueued(snapshot);

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

        private static IEnumerable<FunctionParameterViewModel> CreateParameters(FunctionDefinition function, FunctionInstanceSnapshot snapshot)
        {
            List<FunctionParameterViewModel> parameters = new List<FunctionParameterViewModel>();

            foreach (ParameterStaticBinding binding in function.Flow.Bindings)
            {
                FunctionParameterViewModel parameter = new FunctionParameterViewModel
                {
                    Name = binding.Name,
                    Description = binding.Prompt,
                    Value = snapshot.Arguments[binding.Name].Value
                };
                parameters.Add(parameter);
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
            FunctionInstanceSnapshot ignored;
            return GetFunctionFromInstance(id, out parsed, out ignored);
        }

        private FunctionDefinition GetFunctionFromInstance(string id, out Guid parsed, out FunctionInstanceSnapshot snapshot)
        {
            if (!Guid.TryParse(id, out parsed))
            {
                snapshot = null;
                return null;
            }

            snapshot = _functionInstanceLookup.Lookup(parsed);

            if (snapshot == null)
            {
                return null;
            }

            return GetFunction(snapshot.FunctionId);
        }

        public ActionResult SearchBlob(string path)
        {
            ViewBag.Path = path;

            if (String.IsNullOrEmpty(path))
            {
                return View();
            }

            if (_account == null)
            {
                TempData["Message.Text"] = "Account not found";
                TempData["Message.Level"] = "danger";
                return View();
            }

            ICloudBlob blob;

            try
            {
                var p = new CloudBlobPath(path.Trim());
                blob = p.Resolve(_account);
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
