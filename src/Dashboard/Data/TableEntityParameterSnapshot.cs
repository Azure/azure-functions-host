// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using Microsoft.Azure.WebJobs.Protocols;

namespace Dashboard.Data
{
    [JsonTypeName("TableEntity")]
    public class TableEntityParameterSnapshot : ParameterSnapshot
    {
        public string TableName { get; set; }

        public string PartitionKey { get; set; }

        public string RowKey { get; set; }

        public override string Description
        {
            get
            {
                return String.Format(CultureInfo.CurrentCulture,
                    "Access table entity: {0} (PK: {1}, RK: {2})", TableName, PartitionKey, RowKey);
            }
        }

        public override string AttributeText
        {
            get
            {
                return String.Format(CultureInfo.CurrentCulture,
                    "[Table(\"{0}\", \"{1}\", \"{2}\")]", TableName, PartitionKey, RowKey);
            }
        }

        public override string Prompt
        {
            get
            {
                return "Enter the table entity identifier (TableName/PartitionKey/RowKey)";
            }
        }

        public override string DefaultValue
        {
            get
            {
                if (HasRouteParameter(TableName) || HasRouteParameter(PartitionKey) || HasRouteParameter(RowKey))
                {
                    return null;
                }
                else
                {
                    return String.Format(CultureInfo.CurrentCulture, "{0}/{1}/{2}", TableName, PartitionKey, RowKey);
                }
            }
        }

        private static bool HasRouteParameter(string value)
        {
            return TableEntityParameterSnapshot.HasRouteParameter(value);
        }
    }
}
