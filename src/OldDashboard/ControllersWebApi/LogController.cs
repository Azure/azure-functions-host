using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.StorageClient;

namespace Dashboard.ControllersWebApi
{
    public class LogController : ApiController
    {
        private readonly Services _services;

        internal LogController(Services services)
        {
            _services = services;
        }

        private Services GetServices()
        {
            return _services;
        }

        [HttpGet]
        public HttpResponseMessage InvokeLog(string id)
        {
            // Parse the ID
            Guid funcId;
            if (!Guid.TryParse(id, out funcId))
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            // Get the invocation log
            var instance = GetServices().GetFunctionInstanceLookup().Lookup(funcId);
            if (instance == null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            // Load the blob
            CloudBlob blob;
            try
            {
                blob = new CloudBlob(instance.OutputUrl, GetServices().Account.Credentials);
            }
            catch (Exception)
            {
                blob = null;
            }
            if (blob == null)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            // Get a SAS for the next 10 mins
            string sas = blob.GetSharedAccessSignature(new SharedAccessPolicy()
            {
                Permissions = SharedAccessPermissions.Read,
                SharedAccessExpiryTime = DateTime.UtcNow.AddMinutes(10)
            });

            // Redirect to it
            var resp = new HttpResponseMessage(HttpStatusCode.Found);
            resp.Headers.Location = new Uri(blob.Uri.AbsoluteUri + sas);
            return resp;
        }

        [HttpGet]
        public HttpResponseMessage GetFunctionLog(int N = 20, string account = null)
        {
            LogAnalysis l = new LogAnalysis();
            IFunctionInstanceQuery query = GetServices().GetFunctionInstanceQuery();
            IEnumerable<ChargebackRow> logs = l.GetChargebackLog(N, account, query);

            using (var tw = new StringWriter())
            {
                tw.WriteLine("Name, Id, ParentId, GroupId, FirstParam, Duration");
                foreach (var row in logs)
                {
                    // Sanitize the first parameter for CSV usage. 
                    string val = row.FirstParam;
                    if (val != null)
                    {
                        val = val.Replace('\r', ' ').Replace('\n', ' ').Replace(',', ';');
                    }

                    tw.WriteLine("{0}, {1}, {2}, {3}, {4}, {5}",
                        row.Name,
                        row.Id,
                        row.ParentId,
                        row.GroupId,
                        val,
                        row.Duration);
                }

                var content = tw.ToString();


                var httpContent = new StringContent(content, System.Text.Encoding.UTF8, @"text/csv");
                var resp = new HttpResponseMessage { Content = httpContent };
                return resp;
            }
        }
    }
}
