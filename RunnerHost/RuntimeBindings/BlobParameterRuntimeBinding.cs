using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;
using SimpleBatch;

namespace RunnerHost
{
    // Argument is single blob.
    public class BlobParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public CloudBlobDescriptor Blob { get; set; }

        public override BindResult Bind(IConfiguration config, IBinder bindingContext, ParameterInfo targetParameter)
        {
            bool input = Binder.IsInputParameter(targetParameter);            

            var type = targetParameter.ParameterType;

            if (targetParameter.IsOut)
            {
                if (input)
                {
                    throw new InvalidOperationException("Input blob paramater can't have [Out] keyword");
                }
                type = type.GetElementType();
            }

            return Bind(config, bindingContext, type, input);
        }

        public BindResult Bind(IConfiguration config, IBinder bindingContext, Type type, bool input)
        {            
            ICloudBlobBinder blobBinder = config.GetBlobBinder(type, input);
            if (blobBinder == null)
            {
                throw new InvalidOperationException(string.Format("Not supported binding to a parameter of type '{0}'", type.FullName));
            }

            CloudBlob blob = this.Blob.GetBlob();

            // Verify that blob exists. Give a friendly error up front.
            if (input && !Utility.DoesBlobExist(blob))
            {
                string msg = string.Format("Input blob is not found: {0}", blob.Uri);
                throw new InvalidOperationException(msg);                    
            }

            return blobBinder.Bind(bindingContext, this.Blob.ContainerName, this.Blob.BlobName, type);            
        }
                        
        public override string ConvertToInvokeString()
        {
            CloudBlobPath path = new CloudBlobPath(this.Blob); // stip account
            return path.ToString();
        }

        public override DateTime? LastModifiedTime
        {
            get
            {
                var blob = Blob.GetBlob();
                var time = Utility.GetBlobModifiedUtcTime(blob);
                return time;
            }
        }
    }

    public class BlobAggregateParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public CloudBlobDescriptor BlobPathPattern { get; set; }

        // Descriptor has wildcards in it. 
        // "container\{name}.csv" --> Stream[] all blobs that match
        // 
        public override BindResult Bind(IConfiguration config, IBinder bindingContext, ParameterInfo targetParameter)
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
                        
            for (int i = 0; i < len; i++)
            {
                var b = blobs[i];
                BindResult bind = blobBinder.Bind(bindingContext, b.Container.Name, b.Name, tElement);
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
