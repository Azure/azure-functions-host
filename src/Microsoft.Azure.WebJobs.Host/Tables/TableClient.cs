// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    // Functions for working with azure tables.
    // See http://msdn.microsoft.com/en-us/library/windowsazure/dd179338.aspx
    //
    // Naming rules:
    // RowKey  - no \,/, #, ?, less than 1 kb in size
    // Table name is restrictive, must match: "^[A-Za-z][A-Za-z0-9]{2,62}$"
    internal static class TableClient
    {
        private static readonly char[] _invalidKeyValueCharacters;

        static TableClient()
        {
            _invalidKeyValueCharacters = GetInvalidTableKeyValueCharacters();
        }

        public static string GetAccountName(CloudTableClient client)
        {
            if (client == null)
            {
                return null;
            }

            return StorageClient.GetAccountName(client.Credentials);
        }

        // http://msdn.microsoft.com/en-us/library/windowsazure/dd179338.aspx
        private static char[] GetInvalidTableKeyValueCharacters()
        {
            List<char> invalidCharacters = new List<char>(new char[] { '/', '\\', '#', '?' });

            // U+0000 through U+001F, inclusive
            for (char invalidCharacter = '\x0000'; invalidCharacter <= '\x001F'; invalidCharacter++)
            {
                invalidCharacters.Add(invalidCharacter);
            }

            // U+007F through U+009F, inclusive
            for (char invalidCharacter = '\x007F'; invalidCharacter <= '\x009F'; invalidCharacter++)
            {
                invalidCharacters.Add(invalidCharacter);
            }

            return invalidCharacters.ToArray();
        }

        public static bool ImplementsITableEntity(Type entityType)
        {
            Debug.Assert(entityType != null);
            return entityType.GetInterfaces().Any(t => t == typeof(ITableEntity));
        }

        public static void VerifyDefaultConstructor(Type entityType)
        {
            if (entityType.GetConstructor(Type.EmptyTypes) == null)
            {
                throw new InvalidOperationException("Table entity types must provide a default constructor.");
            }
        }

        // Azure table names are very restrictive, so sanity check upfront to give a useful error.
        // http://msdn.microsoft.com/en-us/library/windowsazure/dd179338.aspx
        public static void ValidateAzureTableName(string tableName)
        {
            if (!IsValidAzureTableName(tableName))
            {
                throw new InvalidOperationException(string.Format("'{0}' is not a valid name for an azure table", tableName));
            }
        }

        public static bool IsValidAzureTableName(string tableName)
        {
            return Regex.IsMatch(tableName, "^[A-Za-z][A-Za-z0-9]{2,62}$");
        }

        // Azure table partition key and row key values are restrictive, so sanity check upfront to give a useful error.
        public static void ValidateAzureTableKeyValue(string value)
        {
            if (!IsValidAzureTableKeyValue(value))
            {
                throw new InvalidOperationException(String.Format(CultureInfo.CurrentCulture,
                    "'{0}' is not a valid value for a partition key or row key.", value));
            }
        }

        public static bool IsValidAzureTableKeyValue(string value)
        {
            // Empty strings and whitespace are valid partition keys and row keys, but null is invalid.
            if (value == null)
            {
                return false;
            }

            return value.IndexOfAny(_invalidKeyValueCharacters) == -1;
        }
    }
}
