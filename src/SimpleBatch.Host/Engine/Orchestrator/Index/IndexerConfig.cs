using System;
using System.Collections.Generic;
using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    // IConfiguration implementation for Registering functions. 
    internal class IndexerConfig : IConfiguration
    {
        private readonly List<FunctionDefinition> _functions = new List<FunctionDefinition>();

        private readonly Func<string, MethodInfo> _fpFuncLookup;

        public IndexerConfig(Func<string, MethodInfo> fpFuncLookup)
        {
            _fpFuncLookup = fpFuncLookup;
        }

        List<ICloudBlobBinderProvider> _blobBinders = new List<ICloudBlobBinderProvider>();
        public IList<ICloudBlobBinderProvider> BlobBinders
        {
            get { return _blobBinders; }
        }

        List<ICloudTableBinderProvider> _tableBinders = new List<ICloudTableBinderProvider>();
        public IList<ICloudTableBinderProvider> TableBinders
        {
            get { return _tableBinders; }
        }

        List<ICloudBinderProvider> _binders = new List<ICloudBinderProvider>();
        public IList<ICloudBinderProvider> Binders
        {
            get { return _binders; }
        }
    }
}
