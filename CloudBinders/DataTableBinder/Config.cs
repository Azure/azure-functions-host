using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SimpleBatch;

namespace DataTableBinder
{
    public class Config
    {
        public static void Initialize(IConfiguration config)
        {
            // provider serves both MutableDataTable and DataTable base class.
            config.BlobBinders.Add(new MutableDataTableBinderProvider());

            // Handle IEnumerable<T> as a DataTable.RowAs<T>() 
            config.BlobBinders.Add(new EnumerableBlobBinderProvider());
        }
    }
}
