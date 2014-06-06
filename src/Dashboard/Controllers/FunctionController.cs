using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Web.Mvc;
using Dashboard.ApiControllers;
using Dashboard.Data;
using Dashboard.HostMessaging;
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
        private readonly IFunctionLookup _functionLookup;
        private readonly IFunctionInstanceLookup _functionInstanceLookup;
        private readonly IFunctionQueuedLogger _functionQueuedLogger;
        private readonly IHeartbeatMonitor _heartbeatMonitor;
        private readonly IInvoker _invoker;

        internal FunctionController(
            CloudStorageAccount account,
            IFunctionLookup functionLookup,
            IFunctionInstanceLookup functionInstanceLookup,
            IFunctionQueuedLogger functionQueuedLogger,
            IHeartbeatMonitor heartbeatMonitor,
            IInvoker invoker
            )
        {
            _account = account;
            _functionLookup = functionLookup;
            _functionInstanceLookup = functionInstanceLookup;
            _functionQueuedLogger = functionQueuedLogger;
            _heartbeatMonitor = heartbeatMonitor;
            _invoker = invoker;
        }

        public ActionResult Run(string functionId)
        {
            if (functionId == null)
            {
                return HttpNotFound();
            }

            FunctionSnapshot function = GetFunction(functionId);

            if (function == null)
            {
                return HttpNotFound();
            }

            var model = CreateRunFunctionViewModel(function, CreateParameters(function), "Run", null);

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Run(string queue, string functionId, FormCollection form)
        {
            FunctionSnapshot function = GetFunction(functionId);

            return Invoke(queue, form, function, TriggerAndOverrideMessageReasons.RunFromDashboard, null);
        }

        public ActionResult Replay(string parentId)
        {
            if (parentId == null)
            {
                return HttpNotFound();
            }

            Guid parent;
            FunctionInstanceSnapshot parentLog;
            FunctionSnapshot function = GetFunctionFromInstance(parentId, out parent, out parentLog);

            if (function == null)
            {
                return HttpNotFound();
            }

            var model = CreateRunFunctionViewModel(function, CreateParameters(function, parentLog), "Replay", parent);

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Replay(string parentId, string queue, FormCollection form)
        {
            Guid parent;
            FunctionSnapshot function = GetFunctionFromInstance(parentId, out parent);

            return Invoke(queue, form, function, TriggerAndOverrideMessageReasons.ReplayFromDashboard, parent);
        }

        private RunFunctionViewModel CreateRunFunctionViewModel(FunctionSnapshot function, IEnumerable<FunctionParameterViewModel> parameters, string submitText, Guid? parentId)
        {
            return new RunFunctionViewModel
            {
                QueueName = function.QueueName,
                FunctionId = function.Id,
                Parameters = parameters,
                ParentId = parentId,
                FunctionName = function.ShortName,
                FunctionFullName = function.FullName,
                HostIsNotRunning = !FunctionsController.HostHasHeartbeat(_heartbeatMonitor, function).GetValueOrDefault(true),
                SubmitText = submitText
            };
        }

        private ActionResult Invoke(string queueName, FormCollection form, FunctionSnapshot function, string reason, Guid? parentId)
        {
            if (function == null)
            {
                return HttpNotFound();
            }

            IDictionary<string, string> arguments = GetArguments(form);

            Guid id = _invoker.TriggerAndOverride(queueName, function, arguments, parentId, reason);

            return Redirect("~/#/functions/invocations/" + id);
        }

        private static IEnumerable<FunctionParameterViewModel> CreateParameters(FunctionSnapshot function)
        {
            List<FunctionParameterViewModel> parameters = new List<FunctionParameterViewModel>();

            foreach (KeyValuePair<string, ParameterSnapshot> parameter in function.Parameters)
            {
                FunctionParameterViewModel parameterModel = new FunctionParameterViewModel
                {
                    Name = parameter.Key,
                    Description = parameter.Value.Prompt,
                    Value = parameter.Value.DefaultValue
                };
                parameters.Add(parameterModel);
            }

            return parameters;
        }

        private static IEnumerable<FunctionParameterViewModel> CreateParameters(FunctionSnapshot function, FunctionInstanceSnapshot snapshot)
        {
            List<FunctionParameterViewModel> parameters = new List<FunctionParameterViewModel>();

            foreach (KeyValuePair<string, ParameterSnapshot> parameter in function.Parameters)
            {
                FunctionParameterViewModel parameterModel = new FunctionParameterViewModel
                {
                    Name = parameter.Key,
                    Description = parameter.Value.Prompt,
                    Value = snapshot.Arguments[parameter.Key].Value
                };
                parameters.Add(parameterModel);
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

        private FunctionSnapshot GetFunction(string functionId)
        {
            return _functionLookup.Read(functionId);
        }

        private FunctionSnapshot GetFunctionFromInstance(string id, out Guid parsed)
        {
            FunctionInstanceSnapshot ignored;
            return GetFunctionFromInstance(id, out parsed, out ignored);
        }

        private FunctionSnapshot GetFunctionFromInstance(string id, out Guid parsed, out FunctionInstanceSnapshot snapshot)
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
                guid = BlobCausalityLogger.GetWriter(blob);
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
