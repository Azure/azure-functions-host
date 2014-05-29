using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs
{
    // Side-effects are understood. We'll read/write to a specific blob, 
    // for which we can even get a modification timestamp from.
    internal class BlobParameterStaticBinding : ParameterStaticBinding
    {
        public CloudBlobPath Path;
        public bool IsInput;

        // $$$ Ratioanlize these rules with BlobParameterRuntimeBinding
        public override void Validate(IConfiguration config, System.Reflection.ParameterInfo parameter)
        {
            BlobClient.ValidateContainerName(this.Path.ContainerName);

            Type type = BlobParameterRuntimeBinding.GetBinderType(parameter, this.IsInput);
            ICloudBlobBinder blobBinder = config.GetBlobBinder(type, IsInput);

            BlobParameterRuntimeBinding.VerifyBinder(type, blobBinder);
        }

        public override ParameterRuntimeBinding Bind(IRuntimeBindingInputs inputs)
        {
            CloudBlobPath path = Path.ApplyNames(inputs.NameParameters);
            return Bind(inputs, path);            
        }

        public override ParameterRuntimeBinding BindFromInvokeString(IRuntimeBindingInputs inputs, string invokeString)
        {
            var path = (string.IsNullOrWhiteSpace(invokeString) && !Path.HasParameters()) ? this.Path : new CloudBlobPath(invokeString);

            return Bind(inputs, path);
        }

        private ParameterRuntimeBinding Bind(IRuntimeBindingInputs inputs, CloudBlobPath path)
        {
            var arg = new CloudBlobDescriptor
            {
                AccountConnectionString = inputs.StorageConnectionString,
                ContainerName = path.ContainerName,
                BlobName = path.BlobName
            };

            BlobClient.ValidateContainerName(arg.ContainerName);

            return new BlobParameterRuntimeBinding { Name = Name, Blob = arg, IsInput = IsInput };
        }

        public override IEnumerable<string> ProducedRouteParameters
        {
            get
            {
                return Path.GetParameterNames();
            }
        }

        public override ParameterDescriptor ToParameterDescriptor()
        {
            return new BlobParameterDescriptor
            {
                ContainerName = Path.ContainerName,
                BlobName = Path.BlobName,
                IsInput = IsInput
            };
        }
    }
}
