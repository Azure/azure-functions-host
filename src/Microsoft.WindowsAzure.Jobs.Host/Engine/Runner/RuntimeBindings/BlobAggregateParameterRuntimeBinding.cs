using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class BlobAggregateParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public CloudBlobDescriptor BlobPathPattern { get; set; }

        // Descriptor has wildcards in it. 
        // "container\{name}.csv" --> Stream[] all blobs that match
        // 
        public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
        {
            var t = targetParameter.ParameterType;
            if (!t.IsArray)
            {
                throw new InvalidOperationException("Matching to multiple blobs requires the parameter be an array type");
            }
            if (t.GetArrayRank() != 1)
            {
                throw new InvalidOperationException("Array must be single dimension");
            }
            var tElement = t.GetElementType(); // Inner
            
            ICloudBlobBinder blobBinder = config.GetBlobBinder(tElement, isInput: true);
            if (blobBinder == null)
            {
                throw new NotImplementedException(string.Format("Not supported binding to a parameter of type '{0}'", tElement.FullName));
            }

            // Need to do enumeration
            // Collect values. 
            Dictionary<string, List<string>> dictParams = new Dictionary<string, List<string>>();
            List<CloudBlob> blobs = new List<CloudBlob>();

            Enumerate(this.BlobPathPattern, blobs, dictParams);

            int len = blobs.Count;

            var array = new BindArrayResult(len, tElement);

            IBlobCausalityLogger logger = new BlobCausalityLogger();

            for (int i = 0; i < len; i++)
            {
                var b = blobs[i];


                const bool isInput = true;
                BindResult bind = BlobBindResult.BindWrapper(isInput, blobBinder, bindingContext, tElement, b, logger);            
                array.SetBind(i, bind);                
            }

            return array;
        }

        // Will populate list and dictionary on return
        private static void Enumerate(CloudBlobDescriptor cloudBlobDescriptor, List<CloudBlob> blobs, Dictionary<string, List<string>> dictParams)
        {
            var patternPath = new CloudBlobPath(cloudBlobDescriptor);

            foreach (var match in patternPath.ListBlobs(cloudBlobDescriptor.GetAccount()))
            {
                var p = patternPath.Match(new CloudBlobPath(match)); // p != null, only matches are returned.

                blobs.Add(match);
                foreach (var kv in p)
                {
                    dictParams.GetOrCreate(kv.Key).Add(kv.Value);
                }
            }
        }

        public override string ConvertToInvokeString()
        {
            CloudBlobPath path = new CloudBlobPath(this.BlobPathPattern); // strip account
            return path.ToString();
        }
    }
}
