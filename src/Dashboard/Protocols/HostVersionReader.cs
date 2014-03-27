using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Dashboard.Protocols
{
    internal class HostVersionReader : IHostVersionReader
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
            List<HostVersion> versions = new List<HostVersion>();
                        
            IEnumerable<IListBlobItem> lazyItems = _container.ListBlobs(
                useFlatBlobListing: true, blobListingDetails: BlobListingDetails.Metadata);
            IListBlobItem[] items;

            try
            {
                items = lazyItems.ToArray();
            }
            catch (StorageException ex)
            {
                // A non-existent container should be treated just like an empty container.
                if (ex.RequestInformation.HttpStatusCode == 404)
                {
                    return new HostVersion[0];
                }
                else
                {
                    throw;
                }
            }

            foreach (ICloudBlob blob in items)
            {
                HostVersion version = GetHostVersion(blob);
                versions.Add(version);
            }

            return versions.ToArray();
        }

        private static HostVersion GetHostVersion(ICloudBlob blob)
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
