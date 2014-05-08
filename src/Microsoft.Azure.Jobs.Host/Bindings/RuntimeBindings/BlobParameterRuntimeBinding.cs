using System;
using System.Reflection;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs
{
    // Argument is single blob.
    internal class BlobParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public CloudBlobDescriptor Blob { get; set; }
        public bool IsInput { get; set; }

        public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
        {
            bool useLease;
            Type type = GetBinderType(targetParameter, this.IsInput, out useLease);                       

            return Bind(config, bindingContext, type, useLease);
        }

        static internal Type GetBinderType(ParameterInfo targetParameter, bool IsInput, out bool useLease)
        {
            var type = targetParameter.ParameterType;

            // Unwrap T& --> T
            // don't forget - IsByRef is true for both 'ref' and 'out' parameters
            if (type.IsByRef)
            {
                type = type.GetElementType();
            } 

            if (targetParameter.IsOut)
            {
                if (IsInput)
                {
                    throw new InvalidOperationException("Input blob parameter can't have [Out] keyword.");
                }
            }

            var isRefKeyword = Utility.IsRefKeyword(targetParameter);
            if (isRefKeyword)
            {
                throw new InvalidOperationException("Input blob parameter can't have [Ref] keyword.");
            }

            // TODO: 'ref' support was cut from alpha1, will be reinstated later
            useLease = false;

            return type;
        }

        public BindResult Bind(IConfiguration config, IBinderEx bindingContext, Type type, bool useLease)
        {            
            ICloudBlobBinder blobBinder = config.GetBlobBinder(type, IsInput);

            JsonByRefBlobBinder leaseAwareBinder = null;

            // $$$ Generalize Blob Lease support to all types. This requires passing the lease Id to the upload function. 
            VerifyBinder(type, blobBinder, useLease);

            if (useLease)
            {
                leaseAwareBinder = new JsonByRefBlobBinder();
                blobBinder = leaseAwareBinder;
            }

            ICloudBlob blob = this.Blob.GetBlob();
            IBlobLeaseHolder _holder = BlobLeaseTestHook();

            if (useLease)
            {
                // If blob doesn't exist yet, we can't lease it. So write out an empty blob. 
                try
                {
                    if (!BlobClient.DoesBlobExist(blob))
                    {
                        // Ok to have multiple workers contend here. One will win. We all need to go through a singel Acquire() point below.
                        blob.UploadFromByteArray(new byte[0], 0, 0);
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

        internal static void VerifyBinder(Type type, ICloudBlobBinder blobBinder, bool useLease)
        {
            if (useLease)
            {
                if (blobBinder != null)
                {
                    string msg = string.Format("The binder for {0} type does not support the ByRef keyword.", type.FullName);
                    throw new NotImplementedException(msg);
                }
            }
            else
            {
                if (blobBinder == null)
                {
                    throw new InvalidOperationException(string.Format("Not supported binding to a parameter of type '{0}'", type.FullName));
                }
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
    }
}
