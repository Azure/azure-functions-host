// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Azure.WebJobs.Protocols;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Dashboard.Data
{
    /// <summary>Represents a reader that provides host version information.</summary>
    public class HostVersionReader : IHostVersionReader
    {
        private readonly CloudBlobDirectory _directory;

        /// <summary>
        /// Instantiates a new instance of the <see cref="HostVersionReader"/> class.
        /// </summary>
        /// <param name="account">The cloud storage account.</param>
        [SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0"), CLSCompliant(false)]
        public HostVersionReader(CloudBlobClient client)
            : this(client.GetContainerReference(DashboardContainerNames.Dashboard)
            .GetDirectoryReference(DashboardDirectoryNames.Versions))
        {
        }

        private HostVersionReader(CloudBlobDirectory directory)
        {
            if (directory == null)
            {
                throw new ArgumentNullException("directory");
            }

            _directory = directory;
        }

        /// <inheritdoc />
        [DebuggerNonUserCode]
        public HostVersion[] ReadAll()
        {
            List<HostVersion> versions = new List<HostVersion>();
                        
            IEnumerable<IListBlobItem> lazyItems = _directory.ListBlobs(
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
                Stream stream = blob.OpenRead();
                try
                {
                    using (TextReader textReader = new StreamReader(stream, utf8))
                    {
                        stream = null;
                        value = textReader.ReadToEnd();
                    }
                }
                finally
                {
                    if (stream != null)
                    {
                        stream.Dispose();
                    }
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
            try
            {
                VersionBlobContent content = JsonConvert.DeserializeObject<VersionBlobContent>(value, JsonSerialization.Settings);

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

        [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
        [SuppressMessage("Microsoft.Usage", "CA1816:CallGCSuppressFinalizeCorrectly")]
        private class VersionBlobContent
        {
            public string Link { get; set; }
        }
    }
}
