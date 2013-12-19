namespace Microsoft.WindowsAzure.Jobs
{
    // Function lives on some file-based system. Need to locate (possibly download) the assembly, load the type, and fine the method. 
    internal abstract class FileFunctionLocation : FunctionLocation
    {
        // Entry point into the function 
        // TypeName is relative to Blob
        public string TypeName { get; set; }
        public string MethodName { get; set; }

        public override string GetShortName()
        {
            return this.TypeName + "." + this.MethodName;
        }
    }
}
