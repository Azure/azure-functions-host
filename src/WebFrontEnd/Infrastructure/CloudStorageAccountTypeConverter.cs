using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Web;
using Microsoft.WindowsAzure;

namespace Microsoft.WindowsAzure.Jobs.Dashboard.Infrastructure
{
    public class CloudStorageAccountTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        {
            return sourceType == typeof(string);
        }

        public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
        {
            return destinationType == typeof(string);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)
        {
            string strValue = value as string;
            if (strValue != null)
            {
                return CloudStorageAccount.Parse(strValue);
            }
            throw new NotSupportedException();
        }

        public override object ConvertTo(ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, Type destinationType)
        {
            if(value == null) {
                throw new ArgumentNullException("value");
            }

            CloudStorageAccount account = value as CloudStorageAccount;
            if (account != null && destinationType == typeof(string))
            {
                return account.ToString(exportSecrets: true);
            }
            throw new NotSupportedException();
        }
    }
}