using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Dashboard.Models.Protocol;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.StorageClient;

namespace Dashboard.Controllers
{
    // Controller for viewing details (mainly invocation) on an individual function.
#if !SITE_EXTENSION
    [Authorize]
#endif
    public class FunctionController : Controller
    {
        private readonly IFunctionTableLookup _functionTableLookup;

        internal FunctionController(IFunctionTableLookup functionTableLookup)
        {
            _functionTableLookup = functionTableLookup;
        }

        //
        // GET: /Function/

        // Show all the static information 
        [HttpGet]
        public ActionResult Index(FunctionDefinitionModel func)
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
        public ActionResult Upload(FunctionDefinitionModel func)
        {
            string inputContainerName = GetInputContainer(func.UnderlyingObject);

            if (Request.Files.Count == 1)
            {
                var file = Request.Files[0];
                if (file != null && file.ContentLength > 0)
                {
                    string filename = Path.GetFileName(file.FileName);


                    var client = func.UnderlyingObject.GetAccount().CreateCloudBlobClient();
                    var container = client.GetContainerReference(inputContainerName);
                    var blob = container.GetBlobReference(filename);

                    // Upload the blob
                    blob.UploadFromStream(file.InputStream);

                    // Then set invoke args. 
                    var instance = Worker.GetFunctionInvocation(func.UnderlyingObject, blob);
                    return RenderInvokePageWorker(func, instance.Args);
                }
            }
            return new ContentResult { Content = "Error. Bad upload" };
        }

        // Called when Run is converting from named parameters to full arg instances
        [HttpPost]
        public ActionResult ComputeArgsFromNames(FunctionDefinitionModel func, string[] key)
        {
            // Convert to a function instance. 
            string[] names = func.Flow.GetInputParameters().ToArray();

            Dictionary<string, string> d = new Dictionary<string, string>();
            for (int i = 0; i < key.Length; i++)
            {
                d[names[i]] = key[i];
            }

            // USe orchestrator to do bindings.
            var instance = Worker.GetFunctionInvocation(func.UnderlyingObject, d);

            return RenderInvokePageWorker(func, instance.Args);
        }

        // Called to get a run request that would "replay" a previous execution instance.
        [HttpGet]
        public ActionResult InvokeFunctionReplay(FunctionInvokeRequestModel instance)
        {
            var parentGuid = instance.Id;

            FunctionDefinition func = _functionTableLookup.Lookup(instance.UnderlyingObject.Location);
            return RenderInvokePageWorker(new FunctionDefinitionModel(func), instance.UnderlyingObject.Args, parentGuid);
        }

        // This is the common worker that everything feeds down into.
        // Seed the input dialog with the given arg instances
        private ActionResult RenderInvokePageWorker(FunctionDefinitionModel func, ParameterRuntimeBinding[] args, Guid? replayGuid = null)
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
            model.Parameters = LogAnalysis.GetParamInfo(func.UnderlyingObject);
            if (args != null)
            {
                LogAnalysis.ApplyRuntimeInfo(args, model.Parameters);
            }

            // If function has an input blob, mark the name (for cosmetic purposes)
            // Uploading to this container can trigger the blob. 
            string inputContainerName = GetInputContainer(func.UnderlyingObject);
            if (inputContainerName != null)
            {
                model.UploadContainerName = Utility.GetAccountName(func.UnderlyingObject.GetAccount()) + "/" + inputContainerName;
            }

            // Precede the args
            return View("Index", model);
        }

        private ActionResult RedirectLogFunctionInstance(ExecutionInstanceLogEntity func)
        {
            return RedirectToAction("FunctionInstance", "Log", new { func = func.GetKey() });
        }

    }
}
