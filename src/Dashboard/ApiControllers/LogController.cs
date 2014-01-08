using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Http;
using Microsoft.WindowsAzure;
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
        public HttpResponseMessage Output(string id, int start)
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

        [HttpGet]
        public HttpResponseMessage Blob(string path)
        {
            if (String.IsNullOrEmpty(path))
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            var p = new CloudBlobPath(path);

            CloudStorageAccount account = Utility.GetAccount(_services.AccountConnectionString);

            if (account == null)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            var blob = p.Resolve(account);

            // Get a SAS for the next 10 mins
            string sas = blob.GetSharedAccessSignature(new SharedAccessPolicy
            {
                Permissions = SharedAccessPermissions.Read,
                SharedAccessExpiryTime = DateTime.UtcNow.AddMinutes(10)
            });

            // Redirect to it
            var resp = new HttpResponseMessage(HttpStatusCode.Found);
            resp.Headers.Location = new Uri(blob.Uri.AbsoluteUri + sas);
            return resp;            
        }
    }
}
