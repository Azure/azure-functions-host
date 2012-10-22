using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using RunnerInterfaces;
using SimpleBatch;

namespace RunnerHost
{
    class Configuration : IConfiguration
    {
        IList<ICloudBlobBinderProvider> _blobBinders = new List<ICloudBlobBinderProvider>();

        IList<ICloudTableBinderProvider> _tableBinders = new List<ICloudTableBinderProvider>();

        IList<ICloudBinderProvider> _Binders = new List<ICloudBinderProvider>();
        
        public IList<ICloudBlobBinderProvider> BlobBinders
        {
            get { return _blobBinders; }
        }


        public IList<ICloudBinderProvider> Binders
        {
            get { return _Binders; }
        }


        public IList<ICloudTableBinderProvider> TableBinders
        {
            get { return _tableBinders; }
        }    
    }
}