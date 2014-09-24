// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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

            LocalBlobDescriptor outputBlobDescriptor = instance.OutputBlob;

            if (outputBlobDescriptor == null)
            {
                return NotFound();
            }

            CloudBlockBlob blob = outputBlobDescriptor.GetBlockBlob(_account);

            var sb = new StringBuilder();
            using (var stream = blob.OpenRead())
            {
                using (var sr = new StreamReader(stream))
                {
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

            return new TextResult(sb.ToString(), Request);
        }

        [HttpGet]
        public IHttpActionResult Blob(string path)
        {
            if (String.IsNullOrEmpty(path))
            {
                return Unauthorized();
            }

            if (_account == null)
            {
                return Unauthorized();
            }

            BlobPath parsed = BlobPath.Parse(path);
            LocalBlobDescriptor descriptor = new LocalBlobDescriptor
            {
                ContainerName = parsed.ContainerName,
                BlobName = parsed.BlobName
            };
            var blob = descriptor.GetBlockBlob(_account);

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
