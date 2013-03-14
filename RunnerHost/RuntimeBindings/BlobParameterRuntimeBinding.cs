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
    // Wraps a bind Result and records the blob's authoring function after the blob is written.
    // This preserves causality functionality. 
    class BlobBindResult : BindResult
    {
        private readonly BindResult _inner;
        private readonly Guid _functionWriter;
        private readonly CloudBlob _blob;
        private readonly IBlobCausalityLogger _logger;

        private BlobBindResult(BindResult inner, Guid functionWriter, CloudBlob blob, IBlobCausalityLogger logger)
        {
            _functionWriter = functionWriter;
            _blob = blob;
            _logger = logger;

            _inner = inner;
            this.Result = _inner.Result;
        }

        public override ISelfWatch Watcher
        {
            get
            {
                return _inner.Watcher;
            }
        }

        public override void OnPostAction()
        {
            _inner.Result = this.Result;
            _inner.OnPostAction(); // important, this is what may write the blob. 

            // This is the critical call to record causality. 
            // The entire purpose of this wrapper class is to make this call. 
            // This must be called after the blbo is written, since it may stamp the blob. 
            _logger.SetWriter(_blob, _functionWriter);
        }

        // Get a BindResult for a blob that will stamp the blob with the GUID of the function instance that wrote it. 
        public static BindResult BindWrapper(bool isInput, ICloudBlobBinder blobBinder, IBinderEx bindingContext, Type targetType, CloudBlob blob, IBlobCausalityLogger logger)
        {
            string containerName = blob.Container.Name;
            string blobName = blob.Name;                

            // Invoke the inner binder to create a cloud blob. 
            var inner = blobBinder.Bind(bindingContext, containerName, blobName, targetType);
            if (isInput)
            {
                // Only stamp blobs we write. 
                return inner;
            }
            
            // Now wrap it with a result that will tag it with a Guid. 
            Guid functionWriter = bindingContext.FunctionInstanceGuid;
            return new BlobBindResult(inner, functionWriter, blob, logger);
        }
    }


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
                type = type.GetElementType();
            }

            return Bind(config, bindingContext, type);
        }

        public BindResult Bind(IConfiguration config, IBinderEx bindingContext, Type type)
        {            
            ICloudBlobBinder blobBinder = config.GetBlobBinder(type, IsInput);
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

            IBlobCausalityLogger logger = new BlobCausalityLogger();
            return BlobBindResult.BindWrapper(IsInput, blobBinder, bindingContext, type, blob, logger);            
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
