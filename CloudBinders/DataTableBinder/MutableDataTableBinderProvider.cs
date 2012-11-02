using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DataAccess;
using System.IO;
using SimpleBatch;
using System.Reflection;

namespace DataTableBinder
{
    class MutableDataTableBinderProvider : ICloudBlobBinderProvider
    {
        private static bool IsExcel(string blobName)
        {
            string ext = Path.GetExtension(blobName);
            bool isExcel = (string.Compare(ext, ".xlsx", true) == 0);
            return isExcel ;
        }

        // In-memory mutable version
        class MutableDataTableInputBinder : ICloudBlobBinder
        {
            public BindResult Bind(IBinder bindingContext, string containerName, string blobName, Type targetType)
            {
                BindResult<Stream> input = bindingContext.BindReadStream<Stream>(containerName, blobName); // Shortcut

                MutableDataTable dt = IsExcel(blobName) ?
                    dt = DataTable.New.ReadExcel(input.Result) :  // stream input, requires Excel format                
                    dt = DataTable.New.Read(new StreamReader(input.Result));

                return new BindResult<MutableDataTable>(dt, input);
            }
        }

        // Streaming version 
        class DataTableInputBinder : ICloudBlobBinder
        {
            public BindResult Bind(IBinder bindingContext, string containerName, string blobName, Type targetType)
            {
                BindResult<Stream> input = bindingContext.BindReadStream<Stream>(containerName, blobName); // Shortcut

                DataTable dt = IsExcel(blobName) ?
                    dt = DataTable.New.ReadExcel(input.Result) :  // stream input, requires Excel format                
                    dt = DataTable.New.ReadLazy(input.Result);

                return new BindResult<DataTable>(dt, input);
            }
        }

        class DataTableOutputBinder : ICloudBlobBinder
        {
            public BindResult Bind(IBinder bindingContext, string containerName, string blobName, Type targetType)
            {
                BindResult<Stream> bind = bindingContext.BindWriteStream<Stream>(containerName, blobName);

                return new BindResult<DataTable>(null, bind)
                {
                    Cleanup = dt =>
                    {
                        if (dt != null)
                        {
                            dt.SaveToStream(new StreamWriter(bind.Result));
                        }
                    }
                };
            }
        }

        public ICloudBlobBinder TryGetBinder(Type targetType, bool isInput)
        {
            if (isInput)
            {
                if (targetType == typeof(MutableDataTable))
                {                
                    return new MutableDataTableInputBinder();
                }
                else if (targetType == typeof(DataTable))
                {
                    return new DataTableInputBinder(); // Streaming
                }
            }
            if (!isInput)
            {
                if (typeof(DataTable).IsAssignableFrom(targetType))
                {
                    return new DataTableOutputBinder();
                }
            }
            return null;
        }
    }
}