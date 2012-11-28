using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using DaasEndpoints;
using Executor;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json.Linq;
using Orchestrator;
using RunnerInterfaces;

namespace WebFrontEnd
{
    public class ExecutionController : ApiController
    {
        // caller should throw the exception so we analyze control flow
        private Exception NewUserError(string format, params string[] args)
        {
            string msg = string.Format(format, args);
            var response = this.Request.CreateErrorResponse(HttpStatusCode.BadRequest, msg);
            return new HttpResponseException(response);            
        }

        [HttpGet]
        public void Heartbeat()
        { 
            // Lets tooling verify they have a valid service URL.
        }

        [HttpPost]        
        public void Scan(string func, string container)
        {
            FunctionIndexEntity f = GetServices().Lookup(func);
            if (f == null)
            {
                throw NewUserError("Function not found. Do you need to add it to the index? '{0}'", func);
            }

            var account = f.GetAccount();
            Helpers.ScanBlobDir(GetServices(), account, new CloudBlobPath(container));
        }

        // Execute the given function. 
        // Assumes execution is used via named parameters.
        [HttpPost]
        public BeginRunResult Run(string func)
        {
            FunctionIndexEntity f = GetServices().Lookup(func);
            if (f == null)
            {
                throw NewUserError("Function not found. Do you need to add it to the index? '{0}'", func);                                
            }

            // Get query parameters
            var uri = this.Request.RequestUri;
            Dictionary<string, string> parameters = GetParamsFromQuery(uri);
            parameters.Remove("func"); //  remove query parameter that we added for the function id

            // Bind and queue. 
            // Queue could be an hour deep
            try
            {
                var instance = Orchestrator.Worker.GetFunctionInvocation(f, parameters);
                instance.TriggerReason = string.Format("Explicitly invoked via POST WebAPI.");

                ExecutionInstanceLogEntity result = GetServices().QueueExecutionRequest(instance);

                return new BeginRunResult { Instance = result.FunctionInstance.Id };
            }
            catch (InvalidOperationException ex)
            {
                throw NewUserError(ex.Message);
            }
        }

        [HttpGet]
        public FunctionInstanceStatusResult GetStatus(Guid id)
        {
            FunctionInvokeLogger logger = GetServices().GetFunctionInvokeLogger();
            var instance = logger.Get(id);
            return new FunctionInstanceStatusResult
            {
                 Status = instance.GetStatus(),
                 OutputUrl = instance.OutputUrl,
                 ExceptionMessage = instance.ExceptionMessage,
                 ExceptionType = instance.ExceptionType,
            };
        }

        private static Dictionary<string, string> GetParamsFromQuery(Uri uri)
        {
            NameValueCollection nvc = uri.ParseQueryString();
            Dictionary<string, string> parameters = new Dictionary<string, string>();
            foreach (var key in nvc.AllKeys)
            {
                parameters[key] = nvc[key];
            }
            return parameters;
        }

        [HttpPost]
        public RegisterArgsResponse RegisterFunction(RegisterArgs args)
        {
            var model = RegisterFuncSubmitworker(args.AccountConnectionString, args.ContainerName);
            return new RegisterArgsResponse { ResultUri = model.Writeback.ToString() };
        }

        public class BeginRunResult
        {
            public Guid Instance { get; set; }
        }

        // $$$ Should we just send back ExecutionInstanceLogEntity?
        public class FunctionInstanceStatusResult
        {
            public FunctionInstanceStatus Status { get; set; }

            // For retrieving the console output. 
            // This is incrementally updated.
            public string OutputUrl { get; set; }

            // For failures
            public string ExceptionType { get; set; }
            public string ExceptionMessage { get; set; }
        }


        public class RegisterArgs
        {
            public string AccountConnectionString { get; set; }
            public string ContainerName { get ;set; }
        }

        public class RegisterArgsResponse
        {
            public string ResultUri { get; set; }
        }

        private static Services GetServices()
        {            
            AzureRoleAccountInfo accountInfo = new AzureRoleAccountInfo();
            return new Services(accountInfo);            
        }

        // Container is relative to accountConnectionString
        internal static WebFrontEnd.Controllers.RegisterFuncSubmitModel RegisterFuncSubmitworker(
            string accountConnectionString, string ContainerName)
        {
            string AccountName = Utility.GetAccount(accountConnectionString).Credentials.AccountName;

            var services = GetServices();
#if false
            try
            {
                var userAccount = GetAccount(AccountName, AccountKey);
                CloudBlobClient client = userAccount.CreateCloudBlobClient();                
                CloudBlobContainer container = client.GetContainerReference(ContainerName);
                container.FetchAttributes();                
            }
            catch (Exception e)
            { 
                ModelState.AddModelError(
            }
#endif
            var container = services.GetExecutionLogContainer();
            string blobResultName = string.Format(@"index\{0}\{1}.{2}.txt", AccountName, ContainerName, DateTime.Now.ToFileTimeUtc());
            var blobResults = container.GetBlobReference(blobResultName);

            string sasQueryString = blobResults.GetSharedAccessSignature(
                new SharedAccessPolicy
                {
                    Permissions = SharedAccessPermissions.Read | SharedAccessPermissions.Write,
                    SharedAccessExpiryTime = DateTime.UtcNow.AddMinutes(45)
                });

            IndexRequestPayload payload = new IndexRequestPayload
            {
                ServiceAccountConnectionString = services.AccountConnectionString,
                UserAccountConnectionString = accountConnectionString,
                Blobpath = ContainerName,
                Writeback = blobResults.Uri.AbsoluteUri + sasQueryString
            };

            {
                // Upload some text as a placeholder while the message waits in the queue.
                // Exercise the SAS too before handing it back to the service.
                string msgStart = string.Format(@"Index request for {0}\{1} is in queue and waiting to be processed.", AccountName, ContainerName);
                CloudBlob blob = new CloudBlob(payload.Writeback);
                blob.UploadText(msgStart);
            }

            services.QueueIndexRequest(payload);

            var model = new WebFrontEnd.Controllers.RegisterFuncSubmitModel
            {
                AccountName = AccountName,
                ContainerName = ContainerName,
                Writeback = new Uri(blobResults.Uri.AbsoluteUri)
            };
            return model;
        }       
    }
}