using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

namespace Microsoft.Azure.Jobs
{
    // Class for describing a container and blob (no account info)
    // - Encapsulate path with {name} wildcards to cloud blob. (which is why this isn't just a CloudBlob)
    // - Can also refer to just a container. Or to just a CloudBlobDirectory.
    // - Standardized parser
    // - ToString should be suitable rowkey to coopreate with table indices.    
    // - Serialize to JSON as a single string. 
    [JsonConverter(typeof(CloudBlobPathConverter))]
    internal class CloudBlobPath
    {
        private readonly string _containerName;

        // Name is flattened for blob subdirectories.
        // May have {key} embedded.
        private readonly string _blobName;

        public string ContainerName
        {
            get { return _containerName; }
        }
        public string BlobName
        {
            get { return _blobName; }
        }

        // Parse the string. 
        public CloudBlobPath(string containerName, string blobName)
        {
            BlobClient.ValidateContainerName(containerName);
            if (blobName != null)
            {
                BlobClient.ValidateBlobName(blobName);
            }
            _containerName = containerName;
            _blobName = blobName;
        }

        public CloudBlobPath(string blobInput)
        {
            string containerName, blobName;
            Parser.SplitBlobPath(blobInput, out containerName, out blobName);

            BlobClient.ValidateContainerName(containerName);
            if (blobName != null)
            {
                BlobClient.ValidateBlobName(blobName);
            }
            _containerName = containerName;
            _blobName = blobName;
        }

        // Create arround actual blob. Loses the account information. 
        public CloudBlobPath(ICloudBlob blobInput)
        {
            _containerName = blobInput.Container.Name;
            _blobName = blobInput.Name;
        }

        public CloudBlobPath(CloudBlobDescriptor descriptor)
        {
            _containerName = descriptor.ContainerName;
            _blobName = descriptor.BlobName;
        }

        public static bool TryParse(string input, out CloudBlobPath path)
        {
            path = new CloudBlobPath(input);
            return true;
        }

        public override string ToString()
        {
            if (_blobName == null)
            {
                return _containerName;
            }
            return _containerName + "/" + _blobName;
        }

        public override bool Equals(object obj)
        {
            CloudBlobPath other = obj as CloudBlobPath;
            if (other == null)
            {
                return false;
            }

            return String.Equals(ToString(), other.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        public override int GetHashCode()
        {
            return this.ToString().GetHashCode();
        }

        // Return new path with names filled in. 
        // Throws if any unbound values. 
        public CloudBlobPath ApplyNames(IDictionary<string, string> nameParameters)
        {
            return new CloudBlobPath(RouteParser.ApplyNames(this.ToString(), nameParameters));
        }

        // Check if the actualPath matches against this blob path. 
        // Returns null if no match.
        // Else returns dictionary of captures. 
        public IDictionary<string, string> Match(CloudBlobPath actual)
        {
            return Parser.Match(this.ToString(), actual.ToString());
        }

        // Given a path, return all the keys in the path.
        // Eg "{name},{date}" would return "name" and "date".
        public IEnumerable<string> GetParameterNames()
        {
            return RouteParser.GetParameterNames(this.ToString());
        }

        public bool HasParameters()
        {
            return RouteParser.HasParameterNames(this.ToString());
        }

        public ICloudBlob Resolve(CloudStorageAccount account)
        {
            var client = account.CreateCloudBlobClient();
            return this.Resolve(client);
        }

        public ICloudBlob Resolve(CloudBlobClient client)
        {
            var container = client.GetContainerReference(this.ContainerName);
            var blob = container.GetBlockBlobReference(this.BlobName);
            return blob;
        }

        // List all blobs that match the pattern. 
        public IEnumerable<ICloudBlob> ListBlobs(CloudStorageAccount account)
        {
            CloudBlobContainer container = this.GetContainer(account);

            foreach (ICloudBlob blobItem in container.ListBlobs(useFlatBlobListing: true))
            {
                var path = blobItem.Uri.ToString();

                var subPath = new CloudBlobPath(blobItem);
                var p = this.Match(subPath);
                if (p != null)
                {
                    yield return blobItem;
                }
            }
        }

        public CloudBlobContainer GetContainer(CloudStorageAccount account)
        {
            var client = account.CreateCloudBlobClient();
            CloudBlobContainer container = client.GetContainerReference(this.ContainerName);
            return container;
        }

        private static class Parser
        {
            // If blob names matches actual pattern. 
            // Null if no match 
            public static IDictionary<string, string> Match(string pattern, string actualPath)
            {
                string container1;
                string blob1;

                string container2;
                string blob2;

                SplitBlobPath(pattern, out container1, out blob1); // may just bec container
                SplitBlobPath(actualPath, out container2, out blob2); // should always be full

                // Containers must match
                if (!String.Equals(container1, container2, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                // Pattern is container only. 
                if (blob1 == null)
                {
                    return new Dictionary<string, string>(); // empty dict, no blob parameters                    
                }

                // Special case for extensions 
                // Let "{name}.csv" match against "a.b.csv", where name = "a.b"
                // $$$ This is getting close to a regular expression...
                {
                    if (pattern.Length > 4 && actualPath.Length > 4)
                    {
                        string ext = pattern.Substring(pattern.Length - 4);
                        if (ext[0] == '.')
                        {
                            if (actualPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                            {
                                pattern = pattern.Substring(0, pattern.Length - 4);
                                actualPath = actualPath.Substring(0, actualPath.Length - 4);

                                return Match(pattern, actualPath);
                            }
                        }
                    }
                }

                // Now see if the actual input matches against the pattern

                Dictionary<string, string> namedParams = new Dictionary<string, string>();

                int iPattern = 0;
                int iActual = 0;
                while (true)
                {
                    if ((iActual == blob2.Length) && (iPattern == blob1.Length))
                    {
                        // Success
                        return namedParams;
                    }
                    if ((iActual == blob2.Length) || (iPattern == blob1.Length))
                    {
                        // Finished at different times. Mismatched
                        return null;
                    }


                    char ch = blob1[iPattern];
                    if (ch == '{')
                    {
                        // Start of a named parameter. 
                        int iEnd = blob1.IndexOf('}', iPattern);
                        if (iEnd == -1)
                        {
                            throw new InvalidOperationException("Missing closing bracket");
                        }
                        string name = blob1.Substring(iPattern + 1, iEnd - iPattern - 1);

                        if (iEnd + 1 == blob1.Length)
                        {
                            // '}' was the last character. Match to end of string
                            string valueRestOfLine = blob2.Substring(iActual);
                            namedParams[name] = valueRestOfLine;
                            return namedParams; // Success
                        }
                        char closingCh = blob1[iEnd + 1];

                        // Scan actual 
                        int iActualEnd = blob2.IndexOf(closingCh, iActual);
                        if (iActualEnd == -1)
                        {
                            // Don't match
                            return null;
                        }
                        string value = blob2.Substring(iActual, iActualEnd - iActual);
                        namedParams[name] = value;

                        iPattern = iEnd + 1; // +1 to move past }
                        iActual = iActualEnd;
                    }
                    else
                    {
                        if (ch == blob2[iActual])
                        {
                            // Match
                            iActual++;
                            iPattern++;
                            continue;
                        }
                        else
                        {
                            // Don't match
                            return null;
                        }
                    }
                }

                throw new NotImplementedException();
            }

            public static void SplitBlobPath(string input, out string container, out string blob)
            {
                Debug.Assert(input != null);

                var parts = input.Split(new[] { '/' }, 2);
                if (parts.Length == 1)
                {
                    // No blob name
                    container = input;
                    blob = null;
                    return;
                }

                container = parts[0];
                blob = parts[1];
            }
        } // end class Parser
    }
}
