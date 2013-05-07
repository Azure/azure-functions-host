using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;
using SimpleBatch;

namespace RunnerHost
{
    // Argument is single blob.
    public class BlobParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public CloudBlobDescriptor Blob { get; set; }
        public bool IsInput { get; set; }

        public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
        {
            var type = targetParameter.ParameterType;

            if (targetParameter.IsOut)
            {
                if (IsInput)
                {
                    throw new InvalidOperationException("Input blob paramater can't have [Out] keyword");
                }
            }

            if (type.IsByRef) // Unwrap T& --> T
            {
                type = type.GetElementType();
            }

            bool useLease = Utility.IsRefKeyword(targetParameter);
            if (useLease)
            {
                // This means we had a bad request formed. 
                if (IsInput)
                {
                    throw new InvalidOperationException("Blob is leased, but marked as input-only.");
                }
            }

            return Bind(config, bindingContext, type, useLease);
        }

        public BindResult Bind(IConfiguration config, IBinderEx bindingContext, Type type, bool useLease)
        {            
            ICloudBlobBinder blobBinder = config.GetBlobBinder(type, IsInput);

            JsonByRefBlobBinder leaseAwareBinder = null;

            // $$$ Generalize Blob Lease support to all types. This requires passing the lease Id to the upload function. 
            if (useLease)
            {
                if (blobBinder != null)
                {
                    string msg = string.Format("The binder for {0} type does not support the 'ByRef keyword.", type.FullName);
                    throw new NotImplementedException(msg);
                }
                leaseAwareBinder = new JsonByRefBlobBinder();
                blobBinder = leaseAwareBinder;
            }

            if (blobBinder == null)
            {                
                throw new InvalidOperationException(string.Format("Not supported binding to a parameter of type '{0}'", type.FullName));                
            }

            CloudBlob blob = this.Blob.GetBlob();

            // Verify that blob exists. Give a friendly error up front.
            if (IsInput && !Utility.DoesBlobExist(blob))
            {
                string msg = string.Format("Input blob is not found: {0}", blob.Uri);
                throw new InvalidOperationException(msg);                    
            }

            IBlobLeaseHolder _holder = BlobLeaseTestHook();

            if (useLease)
            {
                // If blob doesn't exist yet, we can't lease it. So write out an empty blob. 
                try
                {
                    if (!Utility.DoesBlobExist(blob))
                    {
                        // Ok to have multiple workers contend here. One will win. We all need to go through a singel Acquire() point below.
                        blob.UploadText("");
                    }
                }
                catch
                {
                }

                _holder.BlockUntilAcquired(blob);
                leaseAwareBinder.Lease = _holder;
            }

            using(_holder)
            {
                IBlobCausalityLogger logger = new BlobCausalityLogger();
                return BlobBindResult.BindWrapper(IsInput, blobBinder, bindingContext, type, blob, logger);
            }
        }

        // Test hook for hooking BlobLeases.
        public static Func<IBlobLeaseHolder> BlobLeaseTestHook = DefaultBlobLeaseTestHook;
        public static IBlobLeaseHolder DefaultBlobLeaseTestHook()
        {
            return new BlobLeaseHolder();
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
