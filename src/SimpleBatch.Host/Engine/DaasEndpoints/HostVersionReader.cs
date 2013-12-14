using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using System.Diagnostics;

namespace Microsoft.WindowsAzure.Jobs
{
    public class HostVersionReader : IHostVersionReader
    {
        private readonly CloudBlobContainer _container;

        public HostVersionReader(CloudBlobContainer container)
        {
            if (container == null)
            {
                throw new ArgumentNullException("container");
            }

            _container = container;
        }

        [DebuggerNonUserCode]
        public HostVersion[] ReadAll()
        {
            BlobRequestOptions options = new BlobRequestOptions
            {
                UseFlatBlobListing = true,
                // Include metadata to minimize network requests for the content type check.
                BlobListingDetails = BlobListingDetails.Metadata
            };

            List<HostVersion> versions = new List<HostVersion>();
                        
            IEnumerable<IListBlobItem> lazyItems = _container.ListBlobs(options);
            IListBlobItem[] items;

            try
            {
                items = lazyItems.ToArray();
            }
            catch (StorageClientException ex)
            {
                // A non-existent container should be treated just like an empty container.
                if (ex.ErrorCode == StorageErrorCode.ContainerNotFound)
                {
                    return new HostVersion[0];
                }
                else
                {
                    throw;
                }
            }

            foreach (CloudBlob blob in items)
            {
                HostVersion version = GetHostVersion(blob);
                versions.Add(version);
            }

            return versions.ToArray();
        }

        private static HostVersion GetHostVersion(CloudBlob blob)
        {
            // Use the blob name as the HostVersion.Name; any HostVersion will have this property set.
            HostVersion version = new HostVersion
            {
                Label = blob.Name
            };

            // Try to get the link property from the blob contents.
            // HostVersion.Link will only be set when the blob matches this schema.
            if (blob.Properties.ContentType == "application/json")
            {
                Encoding utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
                string value;

                using (Stream stream = blob.OpenRead())
                using (TextReader textReader = new StreamReader(stream, utf8))
                {
                    value = textReader.ReadToEnd();
                }

                string link;

                if (TryReadLink(value, out link))
                {
                    version.Link = link;
                }
            }

            return version;
        }

        private static bool TryReadLink(string value, out string link)
        {
            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };

            try
            {
                VersionBlobContent content = JsonConvert.DeserializeObject<VersionBlobContent>(value);

                if (content == null)
                {
                    link = null;
                    return false;
                }

                link = content.Link;
                return true;
            }
            catch (JsonException)
            {
                link = null;
                return false;
            }
        }

        private class VersionBlobContent
        {
            public string Link { get; set; }
        }
    }
}
