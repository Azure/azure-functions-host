using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Http;
using Dashboard.Data;
using Microsoft.Azure.Jobs;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.ApiControllers
{
    [Route("api/log/{action}/{id?}", Name="LogControllerRoute")]
    public class LogController : ApiController
    {
        private readonly IFunctionInstanceLookup _functionInstanceLookup;
        private readonly CloudStorageAccount _account;

        internal LogController(IFunctionInstanceLookup functionInstanceLookup, CloudStorageAccount account)
        {
            _functionInstanceLookup = functionInstanceLookup;
            _account = account;
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
            var instance = _functionInstanceLookup.Lookup(funcId);
            if (instance == null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            // Load the blob
            ICloudBlob blob;
            try
            {
                blob = _account.CreateCloudBlobClient().GetBlobReferenceFromServer(new Uri(instance.OutputBlobUrl));
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

            if (_account == null)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            var blob = p.Resolve(_account);

            // Get a SAS for the next 10 mins
            string sas = blob.GetSharedAccessSignature(new SharedAccessBlobPolicy
            {
                Permissions = SharedAccessBlobPermissions.Read,
                SharedAccessExpiryTime = DateTime.UtcNow.AddMinutes(10)
            });

            // Redirect to it
            var resp = new HttpResponseMessage(HttpStatusCode.Found);
            resp.Headers.Location = new Uri(blob.Uri.AbsoluteUri + sas);
            return resp;            
        }
    }
}
