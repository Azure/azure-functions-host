// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Web.Http;
using Dashboard.Data;
using Dashboard.HostMessaging;
using Dashboard.Results;
using Microsoft.Azure.WebJobs.Protocols;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.ApiControllers
{
    [Route("api/log/{action}/{id?}", Name = "LogControllerRoute")]
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
        public IHttpActionResult Output(string id, int start = 0)
        {
            // Parse the ID
            Guid funcId;
            if (!Guid.TryParse(id, out funcId))
            {
                return BadRequest();
            }

            // Get the invocation log
            var instance = _functionInstanceLookup.Lookup(funcId);
            if (instance == null)
            {
                return NotFound();
            }

            if (instance.InlineOutputText != null)
            {
                return new TextResult(instance.InlineOutputText, Request);
            }

            LocalBlobDescriptor outputBlobDescriptor = instance.OutputBlob;
            if (outputBlobDescriptor == null)
            {
                return NotFound();
            }

            CloudBlockBlob blob = outputBlobDescriptor.GetBlockBlob(_account);

            var sb = new StringBuilder();
            var stream = blob.OpenRead();

            try
            {
                using (var sr = new StreamReader(stream))
                {
                    stream = null;
                    string line = sr.ReadLine();
                    for (int i = 0; line != null; i++, line = sr.ReadLine())
                    {
                        if (i >= start)
                        {
                            sb.AppendLine(line);
                        }
                    }
                }
            }
            finally
            {
                if (stream != null)
                {
                    stream.Dispose();
                }
            }

            return new TextResult(sb.ToString(), Request);
        }

        [HttpGet]
        public IHttpActionResult Blob(string path, string accountName)
        {
            if (String.IsNullOrEmpty(path) ||
                String.IsNullOrEmpty(accountName))
            {
                return Unauthorized();
            }

            // When linking to a blob, we must resolve the account name to handle cases
            // where multiple storage accounts are being used.
            CloudStorageAccount account = AccountProvider.GetAccountByName(accountName);
            if (account == null)
            {
                return Unauthorized();
            }

            BlobPath parsed = BlobPath.Parse(path);
            LocalBlobDescriptor descriptor = new LocalBlobDescriptor
            {
                ContainerName = parsed.ContainerName,
                BlobName = parsed.BlobName
            };
            var blob = descriptor.GetBlockBlob(account);

            // Get a SAS for the next 10 mins
            string sas = blob.GetSharedAccessSignature(new SharedAccessBlobPolicy
            {
                Permissions = SharedAccessBlobPermissions.Read,
                SharedAccessExpiryTime = DateTime.UtcNow.AddMinutes(10)
            });

            // Redirect to it
            return Redirect(blob.Uri.AbsoluteUri + sas);
        }
    }
}
