// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Microsoft.Azure.WebJobs.Logging
{
    internal static class Utility
    {
        // See examples here: http://stackoverflow.com/questions/19972443/azure-table-storage-xml-serialization-for-tablecontinuationtoken 
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
        public static string SerializeToken(TableContinuationToken token)
        {
            if (token == null)
            {
                return null;
            }

            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                using (var xmlWriter = XmlWriter.Create(writer))
                {
                    token.WriteXml(xmlWriter);
                }
                string serialized = writer.ToString();
                var val = EncodeBase64(serialized);
                return val;
            }

           
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
        public static TableContinuationToken DeserializeToken(string token)
        {
            if (!string.IsNullOrWhiteSpace(token))
            {
                var raw = DecodeBase64(token);
                TableContinuationToken contToken = null;

                using (var stringReader = new StringReader(raw))
                {
                    contToken = new TableContinuationToken();
                    using (var xmlReader = XmlReader.Create(stringReader))
                    {
                        contToken.ReadXml(xmlReader);
                    }
                }
                return contToken;
            }
            return null;            
        }

        public static string EncodeBase64(string str)
        {            
            byte[] bytes = Encoding.UTF8.GetBytes(str);
            string base64 = Convert.ToBase64String(bytes);
            return base64;
        }

        public static string DecodeBase64(string base64)
        {
            byte[] bytes = Convert.FromBase64String(base64);
            string str = Encoding.UTF8.GetString(bytes);
            return str;
        }
    }
}
