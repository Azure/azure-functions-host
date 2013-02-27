using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DataAccess;
using SimpleBatch;

namespace DataTableBinder
{
    // Bind IEnumerable<T> using a CSV reader and strong model binder
    class EnumerableBlobBinderProvider : ICloudBlobBinderProvider
    {
        // Bind produces an IEnumerable<T>.
        // RowAs<T> doesn't take System.Type, so we plumb the T through here.  
        private class EnumerableBlobBinder<T> : ICloudBlobBinder where T : class, new()
        {
            public BindResult Bind(IBinderEx binder, string containerName, string blobName, Type targetType)
            {
                BindResult<Stream> input = binder.BindReadStream<Stream>(containerName, blobName); // Shortcut
                Stream blobStream = input.Result;

                var data = DataTable.New.ReadLazy(blobStream);

                IEnumerable<T> rows = data.RowsAs<T>();
                               
                return new BindResult<IEnumerable<T>>(rows, input); 
            }
        }

        public ICloudBlobBinder TryGetBinder(Type targetType, bool isInput)
        {
            if (!isInput)
            {
                return null;
            }

            // Require an exact match to IEnumerable<T>, not just implements IEnmerable<T>.
            var rowType = IsIEnumerableT(targetType);
            if (rowType != null)
            {
                // RowAs<T> doesn't take System.Type, so need to use some reflection. 
                var t = typeof(EnumerableBlobBinder<>);
                var t2 = t.MakeGenericType(rowType);
                var binder = Activator.CreateInstance(t2);
                return (ICloudBlobBinder)binder;
            }
            return null;
        }

        // Get the T from an IEnumerable<T>. 
        internal static Type IsIEnumerableT(Type typeTarget)
        {
            if (typeTarget.IsGenericType)
            {
                var t2 = typeTarget.GetGenericTypeDefinition();
                if (t2 == typeof(IEnumerable<>))
                {
                    // RowAs<T> doesn't take System.Type, so need to use some reflection. 
                    var rowType = typeTarget.GetGenericArguments()[0];
                    return rowType;
                }
            }
            return null;
        }  
    }
}