using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;

namespace RunnerInterfaces
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
            _containerName = containerName;
            _blobName = blobName;
        }

        public CloudBlobPath(string blobInput)
        {
            Parser.Split(blobInput, out _containerName, out _blobName);
        }

        // Create arround actual blob. Loses the account information. 
        public CloudBlobPath(CloudBlob blobInput)
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
            return _containerName + "\\" + _blobName;
        }

        public override bool Equals(object obj)
        {
            CloudBlobPath other = obj as CloudBlobPath;
            if (other == null)
            {
                return false;
            }

            return string.Compare(this.ToString(), other.ToString(), true) == 0;
        }
        public override int GetHashCode()
        {
            return this.ToString().GetHashCode();
        }

        // Return new path with names filled in. 
        // Throws if any unbound values. 
        public CloudBlobPath ApplyNames(IDictionary<string, string> nameParameters)
        {
            return new CloudBlobPath(Parser.ApplyNames(this.ToString(), nameParameters));
        }

        public CloudBlobPath ApplyNamesPartial(IDictionary<string, string> nameParameters)
        {
            return new CloudBlobPath(Parser.ApplyNamesPartial(this.ToString(), nameParameters));
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
            return Parser.GetParameterNames(this.ToString());
        }


        public CloudBlob Resolve(CloudStorageAccount account)
        {
            var client = account.CreateCloudBlobClient();
            var container = client.GetContainerReference(this.ContainerName);
            var blob = container.GetBlobReference(this.BlobName);
            return blob;
        }

        // List all blobs that match the pattern. 
        public IEnumerable<CloudBlob> ListBlobs(CloudStorageAccount account)
        {
            CloudBlobContainer container = this.GetContainer(account);

            var opt = new BlobRequestOptions();
            opt.UseFlatBlobListing = true;
            foreach (var blobItem in container.ListBlobs(opt))
            {
                var path = blobItem.Uri.ToString();
                CloudBlob b = container.GetBlobReference(blobItem.Uri.ToString());

                var subPath = new CloudBlobPath(b);
                var p = this.Match(subPath);
                if (p != null)
                {
                    yield return b;
                }
            }        
        }

        public CloudBlobContainer GetContainer(CloudStorageAccount account)
        {
            var client = account.CreateCloudBlobClient();
            CloudBlobContainer container = client.GetContainerReference(this.ContainerName);
            return container;
        }

        // Interpret this as a CloudBlobDirectory and list blobs in that directory and subdir.
        // ### RAtionalize with ListBlobs. 
        public IEnumerable<CloudBlob> ListBlobsInDir(CloudStorageAccount account)
        {
            var opt = new BlobRequestOptions();
            opt.UseFlatBlobListing = true; // flat

            var container = this.GetContainer(account);
            var dir = this.GetBlobDir(container);
            
            IEnumerable<IListBlobItem> source = (dir == null) ? container.ListBlobs(opt) : dir.ListBlobs(opt);

            var count = source.Count();

            var blobs = source.OfType<CloudBlob>();

            var c2 = blobs.Count();

            return blobs;
        }

        // Can't include {} tokens. Must be a closed string.
        public CloudBlobDirectory GetBlobDir(CloudStorageAccount account)
        {
            return this.GetBlobDir(this.GetContainer(account));
        }

        // Returned directory may not actually exist.
        // Enumerating non-existent dir is just empty, not failure. 
        private CloudBlobDirectory GetBlobDir(CloudBlobContainer container)
        {
            if (this.BlobName == null)
            {
                return null;
            }
            try
            {
                string blobPath = this.BlobName.Replace('/', '\\');
                string[] parts = blobPath.Split('\\');
                                
                var dir = container.GetDirectoryReference(parts[0]);
                for(int i = 1; i < parts.Length; i++)
                {
                    dir = dir.GetSubdirectory(parts[i]);
                }
                return dir;
            }
            catch
            {
                // $$$ Returned directory may not actually exist  and we don't get any failures. 
                string msg = string.Format("Blobpath '{0}' does not exist", this);
                throw new InvalidOperationException(msg);
            }
        }

        private static class Parser
        {
            static Dictionary<string, string> EmptyDict = new Dictionary<string, string>();

            // ApplyNames, but don't fail if there are unbound names
            public static string ApplyNamesPartial(string pattern, IDictionary<string, string> nameParameters)
            {
                nameParameters = nameParameters ?? EmptyDict;
                return ApplyNamesWorker(pattern, nameParameters, allowUnbound: true);
            }

            // Given "daas-test-input\{name}.csv" and a dict {name:bob}, 
            // returns: "daas-test-input\bob.csv"
            public static string ApplyNames(string pattern, IDictionary<string, string> nameParameters)
            {
                return ApplyNamesWorker(pattern, nameParameters, allowUnbound: false);
            }

            private static string ApplyNamesWorker(string pattern, IDictionary<string, string> names, bool allowUnbound)
            {
                StringBuilder sb = new StringBuilder();
                int i = 0;
                while (i < pattern.Length)
                {
                    char ch = pattern[i];
                    if (ch == '{')
                    {
                        // Find closing
                        int iEnd = pattern.IndexOf('}', i);
                        if (iEnd == -1)
                        {
                            throw new InvalidOperationException("Input pattern is not well formed. Missing a closing bracket");
                        }
                        string name = pattern.Substring(i + 1, iEnd - i - 1);
                        string value;
                        names.TryGetValue(name, out value);
                        if (value == null)
                        {
                            if (!allowUnbound)
                            {
                                throw new InvalidOperationException(string.Format("No value for name parameter '{0}'", name));
                            }
                            // preserve the unbound {name} pattern.
                            sb.Append('{');
                            sb.Append(name);
                            sb.Append('}');
                        }
                        else
                        {
                            sb.Append(value);
                        }
                        i = iEnd + 1;
                    }
                    else
                    {
                        sb.Append(ch);
                        i++;
                    }
                }
                return sb.ToString();
            }

            // If blob names matches actual pattern. 
            // Null if no match 
            public static IDictionary<string, string> Match(string pattern, string actualPath)
            {
                pattern = pattern.Replace('/', '\\');
                actualPath = actualPath.Replace('/', '\\');

                string container1;
                string blob1;

                string container2;
                string blob2;

                Split(pattern, out container1, out blob1); // may just bec container
                Split(actualPath, out container2, out blob2); // should always be full

                // Containers must match
                if (string.Compare(container1, container2, true) != 0)
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

            public static void Split(string input, out string container, out string blob)
            {
                input = input.Replace('/', '\\');
                container = null;
                blob = null;

                int i = input.IndexOf('\\');
                if (i <= 0)
                {
                    // No blob name
                    container = input;
                    return;
                }

                container = input.Substring(0, i);
                blob = input.Substring(i + 1);
            }


            // Given a path, return all the keys in the path.
            // Eg "{name},{date}" would return "name" and "date".
            public static IEnumerable<string> GetParameterNames(string pattern)
            {
                List<string> names = new List<string>();

                int i = 0;
                while (i < pattern.Length)
                {
                    char ch = pattern[i];
                    if (ch == '{')
                    {
                        // Find closing
                        int iEnd = pattern.IndexOf('}', i);
                        if (iEnd == -1)
                        {
                            throw new InvalidOperationException("Input pattern is not well formed. Missing a closing bracket");
                        }
                        string name = pattern.Substring(i + 1, iEnd - i - 1);
                        names.Add(name);
                        i = iEnd + 1;
                    }
                    else
                    {
                        i++;
                    }
                }
                return names;
            }
        } // end class PArser
    }

    internal class CloudBlobPathConverter : StringConverter<CloudBlobPath>
    {
        public override object ReadFromString(string value)
        {
            return new CloudBlobPath(value);
        }        
    }
}

