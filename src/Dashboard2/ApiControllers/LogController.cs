using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Http;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.StorageClient;

namespace Dashboard.ApiControllers
{
    public class LogController : ApiController
    {
        private readonly Services _services;

        internal LogController(Services services)
        {
            _services = services;
        }

        [HttpGet]
        public HttpResponseMessage Output(string id, int start = 0)
        {
            // Parse the ID
            Guid funcId;
            if (!Guid.TryParse(id, out funcId))
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            // Get the invocation log
            var instance = _services.GetFunctionInstanceLookup().Lookup(funcId);
            if (instance == null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            // Load the blob
            CloudBlob blob;
            try
            {
                blob = new CloudBlob(instance.OutputUrl, _services.Account.Credentials);
            }
            catch (Exception)
            {
                blob = null;
            }

            if (blob == null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var sb = new StringBuilder();
            using (var stream = blob.OpenRead())
            {
                using (var sr = new StreamReader(stream))
                {
                    string line;
                    var i = 0;
                    do
                    {
                        line = sr.ReadLine();

                        if (i++ > start)
                        {
                            sb.AppendLine(line);
                        }
                     
                    }   while (line != null) ;
                }
            }

            // Redirect to it
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sb.ToString())
            };

            return resp;
        }
    }
}
