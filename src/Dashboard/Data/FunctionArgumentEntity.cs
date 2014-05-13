using System;
using System.Globalization;
using Microsoft.WindowsAzure.Storage.Table;

namespace Dashboard.Data
{
    [CLSCompliant(false)]
    public class FunctionArgumentEntity : TableEntity
    {
        public string Name { get; set; }

        public string Value { get; set; }

        public bool? IsBlob { get; set; }

        public bool? IsBlobInput { get; set; }

        internal static string GetRowKey(Guid functionInstanceId, int index)
        {
            return String.Format(CultureInfo.InvariantCulture, "{0}_Argument_{1}", functionInstanceId, index);
        }
    }
}
