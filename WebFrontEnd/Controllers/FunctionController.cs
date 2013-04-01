using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using DaasEndpoints;
using Executor;
using Orchestrator;
using RunnerInterfaces;

namespace WebFrontEnd.Controllers
{
    // Show static information about the function 
    public class FunctionInfoModel
    {
        public FunctionIndexEntity Descriptor { get; set; }

        public ParamModel[] Parameters { get; set; }

        // List of {name}
        public string[] KeyNames { get; set; }
    }

    // Controller for viewing details (mainly invocation) on an individual function.
    [Authorize]
    public class FunctionController : Controller
    {
        private Services GetServices()
        {
            AzureRoleAccountInfo accountInfo = new AzureRoleAccountInfo();
            return new Services(accountInfo);
        }

        //
        // GET: /Function/

        // Show all the static information 
        [HttpGet]
        public ActionResult Index(FunctionIndexEntity func)
        {
            return RenderInvokePageWorker(func, null);
        }

        // Called when Run is converting from named parameters to full arg instances
        [HttpPost]
        public ActionResult ComputeArgsFromNames(FunctionIndexEntity func, string[] key)
        {
            // Convert to a function instance. 
            string[] names = func.Flow.GetInputParameters().ToArray();

            Dictionary<string, string> d = new Dictionary<string, string>();
            for (int i = 0; i < key.Length; i++)
            {
                d[names[i]] = key[i];
            }

            // USe orchestrator to do bindings.
            var instance = Orchestrator.Worker.GetFunctionInvocation(func, d);

            return RenderInvokePageWorker(func, instance.Args);
        }

        // Called to get a run request that would "replay" a previous execution instance.
        [HttpGet]
        public ActionResult InvokeFunctionReplay(FunctionInvokeRequest instance)
        {
            FunctionIndexEntity func = GetServices().GetFunctionTable().Lookup(instance.Location);
            return RenderInvokePageWorker(func, instance.Args);
        }

        // This is the common worker that everything feeds down into.
        // Seed the input dialog with the given arg instances
        private ActionResult RenderInvokePageWorker(FunctionIndexEntity func, ParameterRuntimeBinding[] args)
        {
            var flows = func.Flow;

            var model = new FunctionInfoModel();
            model.Descriptor = func;
            model.KeyNames = flows.GetInputParameters().ToArray();
            model.Parameters = LogAnalysis.GetParamInfo(func);
            if (args != null)
            {
                LogAnalysis.ApplyRuntimeInfo(args, model.Parameters);
            }

            // Precede the args
            return View("Index", model);
        }

        // Post when we actually submit the invoke. 
        [HttpPost]
        public ActionResult InvokeFunctionWithArgs(FunctionIndexEntity func, string[] argValues)
        {
            if (argValues == null)
            {
                argValues = new string[0];
            }
            ParameterRuntimeBinding[] args = new ParameterRuntimeBinding[argValues.Length];

            var flows = func.Flow.Bindings;
            var account = func.GetAccount();

            IRuntimeBindingInputs inputs = new RunnerHost.RuntimeBindingInputs(func.Location);

            for (int i = 0; i < argValues.Length; i++)
            {
                var flow = flows[i];
                args[i] = flow.BindFromInvokeString(inputs, argValues[i]);

                if (args[i] == null)
                {
                    // ### Error
                }
            }

            FunctionInvokeRequest instance = new FunctionInvokeRequest();
            instance.Args = args;
            instance.Location = func.Location;
            instance.TriggerReason = new InvokeTriggerReason
            {
                Message = "Explicitly requested via web dashboard",
                ParentGuid = Guid.Empty, // Invoked directly by user, so no parent function. 
            };

            // Get instance ID from queuing. Use that to redict to view 
            IQueueFunction executor = GetServices().GetQueueFunction();
            var instanceLog = executor.Queue(instance);

            // We got here via a POST. 
            // Switch to a GET so that users can do a page refresh as the function updates. 
            return RedirectLogFunctionInstance(instanceLog);
        }

        private ActionResult RedirectLogFunctionInstance(ExecutionInstanceLogEntity func)
        {
            return RedirectToAction("FunctionInstance", "Log", new { func = func.RowKey });
        }

    }
}
