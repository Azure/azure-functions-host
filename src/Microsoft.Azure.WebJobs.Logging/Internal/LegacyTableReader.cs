// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Logging
{
    // LogReader needs to handle older format.
    // DO this in one central place so that we can remove this class when deprecate the old format. 
    // One central place 
    internal class LegacyTableReader
    {
        // The previous table name. 
        public const string OldTableName = "AzureFunctionsLogTable";

        //  null if not present
        public static CloudTable GetLegacyTable(CloudTableClient client)
        {
            var table = client.GetTableReference(OldTableName);
            if (!table.Exists())
            {
                return null;
            }
            return table;
        }

        // Does the suffix match the request to get hte legacy table? 
        // Returns null if legacy table doesn't exist. 
        public static CloudTable TryGetLegacy(CloudTableClient client, string suffix)
        {
            if (suffix == OldTableName)
            {
                return GetLegacyTable(client);
            }
            return null;
        }

        // -1 if not matched
        public static long GetEpochFromTable(CloudTable table)
        {
            if (table.Name == OldTableName)
            {
                return 201609; // Epoch number for Sep 2016, corresponding to latest time of old logs. 
            }
            return -1;
        }

        // Returns null if no legacy table.          
        public static CloudTable GetLegacyTable(ILogTableProvider tableProvider)
        {
            var table = tableProvider.GetTable(OldTableName);
            if (!table.Exists())
            {
                return null;
            }
            return table;
        }

        // Merge 2 lists. Giv newer onces precedence over older. 
        public static IFunctionDefinition[] Merge(IFunctionDefinition[] newer, IFunctionDefinition[] older)
        {                        
            var defs = new Dictionary<string, IFunctionDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in older.Concat(newer))
            {
                defs[entry.Name] = entry;
            }

            return defs.Values.ToArray();   
        }
    }
}