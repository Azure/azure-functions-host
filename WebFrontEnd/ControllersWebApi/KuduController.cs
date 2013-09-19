using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using DaasEndpoints;
using DataAccess;
using Orchestrator;
using RunnerInterfaces;
using WebFrontEnd.Controllers;

namespace WebFrontEnd.ControllersWebApi
{
    public class KuduController : ApiController
    {
        // Called after a new kudu site is published and we need to index it. 
        // Uri is for the antares site that we ping. 
        [HttpPost]
        public FuncSubmitModel Index(string uri)
        {
            // At least return a URL that they can view results at.
            // Useful for when debugging with Fiddler.
            return IndexWorker(uri);
        }

        public static FuncSubmitModel IndexWorker(string uri)
        {
            // common case, append the expected route. 
            if (uri.EndsWith(".azurewebsites.net"))
            {
                uri += "/";
            }
            if (uri.EndsWith(".azurewebsites.net/"))
            {
                uri += "api/SimpleBatchIndexer";
            }

            // Ping orchestrator to update maps?
            // Or even send a IndexRequestPayload over with the URL
            var obj = new IndexUrlOperation { Url = uri } ;

            throw new NotImplementedException("Need updated kudu story");
            // return new ExecutionController(null).RegisterFuncSubmitworker(obj);
        }
    }
}