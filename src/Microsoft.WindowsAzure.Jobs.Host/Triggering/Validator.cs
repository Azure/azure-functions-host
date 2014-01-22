using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class Validator
    {
        public static void ValidateBlobTrigger(TriggerRaw trigger)
        {
            // Verify we parse propertly. 
            var inputs = ValidatePath(trigger.BlobInput);

            if (trigger.BlobOutput != null)
            {
                var outputPaths = trigger.BlobOutput.Split(';');
                foreach (var outputPath in outputPaths)
                {
                    // Verify that the output params are a subset of the input params. 
                    var outputs = ValidatePath(outputPath);

                    foreach (var x in inputs)
                    {
                        outputs.Remove(x);
                    }
                    if (outputs.Count > 0)
                    {
                        throw new InvalidOperationException("Unresolved parameters in output blob:" + string.Join(", ", outputs));
                    }
                }
            }
        }

        // validate the path for parsing and return the {} tokens. 
        private static HashSet<string> ValidatePath(string pathString)
        {
            CloudBlobPath path = new CloudBlobPath(pathString);
            ValidateContainerName(path.ContainerName);
            var x = new HashSet<string>(path.GetParameterNames()); // parses blobname

            return x;
        }

        // Naming rules are here: http://msdn.microsoft.com/en-us/library/dd135715.aspx
        // Validate this on the client side so that we can get a user-friendly error rather than a 400.
        // See code here: http://social.msdn.microsoft.com/Forums/en-GB/windowsazuredata/thread/d364761b-6d9d-4c15-8353-46c6719a3392
        public static void ValidateContainerName(string containerName)
        {
            if (containerName == null)
            {
                throw new ArgumentNullException("containerName");
            }
            if (containerName.Equals("$root"))
            {
                return;
            }

            if (!Regex.IsMatch(containerName, @"^[a-z0-9](([a-z0-9\-[^\-])){1,61}[a-z0-9]$"))
            {
                throw new Exception("Invalid container name: " + containerName);
            }
        }
    }
}
