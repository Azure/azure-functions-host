using System;
using System.IO;
using System.Reflection;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs
{
    // Argument is single blob.
    internal class BlobParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public CloudBlobDescriptor Blob { get; set; }

        public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
        {
            Type type = GetBinderType(targetParameter);
            return Bind(config, bindingContext, type);
        }

        static internal Type GetBinderType(ParameterInfo targetParameter)
        {
            var type = targetParameter.ParameterType;

            // Unwrap T& --> T
            // don't forget - IsByRef is true for both 'ref' and 'out' parameters
            if (type.IsByRef)
            {
                type = type.GetElementType();
            }

            var isRefKeyword = Utility.IsRefKeyword(targetParameter);
            if (isRefKeyword)
            {
                throw new InvalidOperationException("Input blob parameter can't have [Ref] keyword.");
            }

            return type;
        }

        public BindResult Bind(IConfiguration config, IBinderEx bindingContext, Type type)
        {
            ICloudBlobBinder blobBinder = config.GetBlobBinder(type);

            VerifyBinder(type, blobBinder);

            ICloudBlob blob = this.Blob.GetBlob();

            IBlobCausalityLogger logger = new BlobCausalityLogger();
            return BlobBindResult.BindWrapper(blobBinder, bindingContext, type, blob, logger);
        }

        internal static void VerifyBinder(Type type, ICloudBlobBinder blobBinder)
        {
            if (blobBinder == null)
            {
                throw new InvalidOperationException(string.Format("Not supported binding to a parameter of type '{0}'", type.FullName));
            }
        }

        public override string ConvertToInvokeString()
        {
            CloudBlobPath path = new CloudBlobPath(this.Blob); // stip account
            return path.ToString();
        }
    }
}
