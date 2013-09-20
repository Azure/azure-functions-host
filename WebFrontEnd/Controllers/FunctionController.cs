using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using DaasEndpoints;
using Executor;
using Orchestrator;
using RunnerInterfaces;
using Microsoft.WindowsAzure.StorageClient;
using System.IO;

namespace WebFrontEnd.Controllers
{
    // Controller for viewing details (mainly invocation) on an individual function.
#if !SITE_EXTENSION
    [Authorize]
#endif
    public class FunctionController : Controller
    {
        private readonly IFunctionTableLookup _functionTableLookup;
        private readonly IQueueFunction _queueFunction;

        public FunctionController(IQueueFunction queueFunction, IFunctionTableLookup functionTableLookup)
        {
            _queueFunction = queueFunction;
            _functionTableLookup = functionTableLookup;
        }

        //
        // GET: /Function/

        // Show all the static information 
        [HttpGet]
        public ActionResult Index(FunctionDefinition func)
        {
            return RenderInvokePageWorker(func, null);
        }

        public static bool HasFile(HttpPostedFileBase file)
        {
            return (file != null && file.ContentLength > 0) ? true : false;
        }

        static string GetInputContainer(FunctionDefinition func)
        {
            foreach (var binding in func.Flow.Bindings)
            {
                BlobParameterStaticBinding b = binding as BlobParameterStaticBinding;
                if (b != null)
                {
                    if (b.IsInput)
                    {
                        return b.Path.ContainerName;
                    }
                }                
            }
            return null;
        }

        [HttpPost]
        public ActionResult Upload(FunctionDefinition func)
        {
            string inputContainerName = GetInputContainer(func);

            if (Request.Files.Count == 1)
            {            
                var file = Request.Files[0];
                if (file != null && file.ContentLength > 0)
                {
                    string filename = Path.GetFileName(file.FileName);


                    var client = func.GetAccount().CreateCloudBlobClient();
                    var container = client.GetContainerReference(inputContainerName);
                    var blob = container.GetBlobReference(filename);

                    // Upload the blob
                    blob.UploadFromStream(file.InputStream);

                    // Then set invoke args. 
                    var instance = Orchestrator.Worker.GetFunctionInvocation(func, blob);
                    return RenderInvokePageWorker(func, instance.Args);
                }
            }

            return new ContentResult { Content = "Error. Bad upload" };           
            
        }

        // Called when Run is converting from named parameters to full arg instances
        [HttpPost]
        public ActionResult ComputeArgsFromNames(FunctionDefinition func, string[] key)
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
            var parentGuid = instance.Id;

            FunctionDefinition func = _functionTableLookup.Lookup(instance.Location);
            return RenderInvokePageWorker(func, instance.Args, parentGuid);
        }

        // This is the common worker that everything feeds down into.
        // Seed the input dialog with the given arg instances
        private ActionResult RenderInvokePageWorker(FunctionDefinition func, ParameterRuntimeBinding[] args, Guid? replayGuid = null)
        {
            if (func == null)
            {
                // ### Give this better UI. Chain to the error on Log.Error
                // Function was probably unloaded from server. 
                return View("Error");
            }
            var flows = func.Flow;

            var model = new FunctionInfoModel();
            if (replayGuid.HasValue)
            {
                model.ReplayGuid = replayGuid.Value;
            }
            model.Descriptor = func;
            model.KeyNames = flows.GetInputParameters().ToArray();
            model.Parameters = LogAnalysis.GetParamInfo(func);
            if (args != null)
            {
                LogAnalysis.ApplyRuntimeInfo(args, model.Parameters);
            }

            // If function has an input blob, mark the name (for cosmetic purposes)
            // Uploading to this container can trigger the blob. 
            string inputContainerName = GetInputContainer(func);
            if (inputContainerName != null)
            {
                model.UploadContainerName = Utility.GetAccountName(func.GetAccount()) + "/" + inputContainerName;
            }

            // Precede the args
            return View("Index", model);
        }

        // Post when we actually submit the invoke. 
        [HttpPost]
        public ActionResult InvokeFunctionWithArgs(FunctionDefinition func, string[] argValues, Guid? replayGuid)
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

            if (replayGuid.HasValue && replayGuid != Guid.Empty)
            {
                instance.TriggerReason = new InvokeTriggerReason
                {
                    Message = "Invoked by replaying a previous function.",
                    ParentGuid = replayGuid.Value
                };
            }
            else
            {
                instance.TriggerReason = new InvokeTriggerReason
                {
                    Message = "Explicitly requested via web dashboard",
                    ParentGuid = Guid.Empty, // Invoked directly by user, so no parent function. 
                };
            }

            // Get instance ID from queuing. Use that to redict to view 
            var instanceLog = _queueFunction.Queue(instance);

            // We got here via a POST. 
            // Switch to a GET so that users can do a page refresh as the function updates. 
            return RedirectLogFunctionInstance(instanceLog);
        }

        private ActionResult RedirectLogFunctionInstance(ExecutionInstanceLogEntity func)
        {
            return RedirectToAction("FunctionInstance", "Log", new { func = func.GetKey() });
        }

    }
}
