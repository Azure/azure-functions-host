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

namespace WebFrontEnd.ControllersWebApi
{
    public class KuduController : ApiController
    {
        private static Services GetServices()
        {
            AzureRoleAccountInfo accountInfo = new AzureRoleAccountInfo();
            return new Services(accountInfo);
        }

        // Called after a new kudu site is published and we need to index it. 
        // Uri is for the antares site that we ping. 
        public void Index(string uri)
        {
            // $$$ Multi-thread race with Orchestrator?            
            var results = Utility.GetJson<FunctionDefinition[]>(uri);

            var services = GetServices();
            IFunctionTable table = services.GetFunctionTable();
            foreach (var func in results)
            {
                table.Add(func);
            }

            // !!! Remove stale functions?

            // Ping orchestrator to update maps?
            // Or even send a IndexRequestPayload over with the URL
        }
    }
}