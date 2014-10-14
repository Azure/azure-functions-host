// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading;
using System.Web.Mvc;
using Dashboard.ApiControllers;
using Dashboard.Data;
using Dashboard.HostMessaging;
using Dashboard.Infrastructure;
using Dashboard.ViewModels;
using Microsoft.Azure.WebJobs.Protocols;
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
        private readonly IHeartbeatValidityMonitor _heartbeatMonitor;
        private readonly IInvoker _invoker;

        internal FunctionController(
            CloudStorageAccount account,
            IFunctionLookup functionLookup,
            IFunctionInstanceLookup functionInstanceLookup,
            IFunctionQueuedLogger functionQueuedLogger,
            IHeartbeatValidityMonitor heartbeatMonitor,
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

        [AcceptVerbs(HttpVerbs.Get | HttpVerbs.Head)]
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

            return Invoke(queue, form, function, ExecutionReason.Dashboard, null);
        }

        [AcceptVerbs(HttpVerbs.Get | HttpVerbs.Head)]
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

            IEnumerable<FunctionParameterViewModel> parameters;
            ViewBag.CanReplay = TryResolveParameters(function, parentLog, out parameters);
            var model = CreateRunFunctionViewModel(function, parameters, "Replay", parent);

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Replay(string parentId, string queue, FormCollection form)
        {
            Guid parent;
            FunctionSnapshot function = GetFunctionFromInstance(parentId, out parent);

            return Invoke(queue, form, function, ExecutionReason.Dashboard, parent);
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

        private ActionResult Invoke(string queueName, FormCollection form, FunctionSnapshot function, ExecutionReason reason, Guid? parentId)
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

        private static bool TryResolveParameters(FunctionSnapshot function, FunctionInstanceSnapshot snapshot, out IEnumerable<FunctionParameterViewModel> resolvedParameters)
        {
            List<FunctionParameterViewModel> parameters = new List<FunctionParameterViewModel>();

            foreach (KeyValuePair<string, ParameterSnapshot> parameter in function.Parameters)
            {
                if (!snapshot.Arguments.ContainsKey(parameter.Key))
                {
                    resolvedParameters = null;
                    return false;
                }
                
                FunctionParameterViewModel parameterModel = new FunctionParameterViewModel
                {
                    Name = parameter.Key,
                    Description = parameter.Value.Prompt,
                    Value = snapshot.Arguments[parameter.Key].Value
                };
                
                parameters.Add(parameterModel);
            }

            resolvedParameters = parameters;
            return true;
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

        [AcceptVerbs(HttpVerbs.Get | HttpVerbs.Head)]
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

            ICloudBlob blob = null;

            try
            {
                BlobPath parsed = BlobPath.Parse(path);
                LocalBlobDescriptor descriptor = new LocalBlobDescriptor
                {
                    ContainerName = parsed.ContainerName,
                    BlobName = parsed.BlobName
                };

                IReadOnlyDictionary<string, CloudStorageAccount> accounts = AccountProvider.GetAccounts();
                foreach (var account in accounts.Values)
                {
                    blob = descriptor.GetBlockBlob(account);
                    if (blob.Exists())
                    {
                        break;
                    }
                    else
                    {
                        blob = null;
                    }
                }
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

            Guid? guid;

            try
            {
                guid = BlobCausalityReader.GetParentId(blob);
            }
            catch
            {
                guid = null;
            }

            if (!guid.HasValue)
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
