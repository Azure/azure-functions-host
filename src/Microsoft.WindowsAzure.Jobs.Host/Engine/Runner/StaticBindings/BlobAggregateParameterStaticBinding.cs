namespace Microsoft.WindowsAzure.Jobs
{
    // Binds to multiple blobs. All blobs that matche the given pattern.
    class BlobAggregateParameterStaticBinding : ParameterStaticBinding
    {
        public CloudBlobPath BlobPathPattern { get; set; }

        public override ParameterRuntimeBinding Bind(IRuntimeBindingInputs inputs)
        {
            // plug in any unbounded parameters. 
            //    deploydId=123
            //    "daas-test-input\{deployId}\{names}.csv"
            //  becomees: "daas-test-input\123\{names}.csv"
            CloudBlobPath path = this.BlobPathPattern.ApplyNamesPartial(inputs.NameParameters);

            // Pass to arg Instance "daas-test-input\123\{names}.csv"
            // RuntimeHost binder gets that, does enumeration (of entire container?) and match. 
            // builds up Dict<string, List<string>>,  [names] = "first","second", "third"
            // Can then bind names to string[] names parameter.

            var binding = new BlobAggregateParameterRuntimeBinding
            {
                BlobPathPattern = new CloudBlobDescriptor
                {
                    AccountConnectionString = inputs.AccountConnectionString,
                    ContainerName = path.ContainerName,
                    BlobName = path.BlobName
                }
            };
            BlobClient.ValidateContainerName(binding.BlobPathPattern.ContainerName);

            return binding;
        }

        public override ParameterRuntimeBinding BindFromInvokeString(IRuntimeBindingInputs inputs, string invokeString)
        {
            var path = new CloudBlobPath(invokeString);

            return new BlobAggregateParameterRuntimeBinding
            {                 
                BlobPathPattern = new CloudBlobDescriptor
                {
                    AccountConnectionString = inputs.AccountConnectionString,
                    ContainerName = path.ContainerName,
                    BlobName = path.BlobName
                }
            };
        }

        public override string Description
        {
            get {
                string msg = string.Format("Read from all blobs matching pattern: {0}", this.BlobPathPattern);
                return msg;
            }
        }
    }
}
