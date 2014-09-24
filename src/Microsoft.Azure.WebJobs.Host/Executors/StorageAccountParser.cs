// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Globalization;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    /// <summary>
    /// Utility class designed to parse given connection string and create instance of the 
    /// <see cref="CloudStorageAccount"/>.
    /// </summary>
    internal sealed class StorageAccountParser
    {
        /// <summary>
        /// Throwing version of parse account API. It calls TryParseAccount internally, analyzes returned result,
        /// and throws an exception with formatted message in case of error
        /// </summary>
        /// <param name="connectionString">A Storage account connection string as retrieved from the config</param>
        /// <param name="connectionStringName">Friendly connection string name used to format error message</param>
        /// <returns>An instance of <see cref="CloudStorageAccount"/> associated with the given connection string</returns>
        public static CloudStorageAccount ParseAccount(string connectionString, string connectionStringName)
        {
            CloudStorageAccount account;
            StorageAccountParseResult result = TryParseAccount(connectionString, out account);

            if (result != StorageAccountParseResult.Success)
            {
                string message = FormatParseAccountErrorMessage(result, connectionStringName);
                throw new InvalidOperationException(message);
            }

            return account;
        }

        /// <summary>
        /// Non-throwing core version of parse account API.
        /// </summary>
        /// <param name="connectionString">A Storage account connection string as retrieved from the JobHost configuration</param>
        /// <param name="account">Out parameter to return instance of <see cref="CloudStorageAccount"/> in case of success</param>
        /// <returns>Error code of parse account attempt</returns>
        public static StorageAccountParseResult TryParseAccount(string connectionString, out CloudStorageAccount account)
        {
            if (String.IsNullOrEmpty(connectionString))
            {
                account = null;
                return StorageAccountParseResult.MissingOrEmptyConnectionStringError;
            }

            CloudStorageAccount possibleAccount;
            if (!CloudStorageAccount.TryParse(connectionString, out possibleAccount))
            {
                account = null;
                return StorageAccountParseResult.MalformedConnectionStringError;
            }

            if (StorageClient.IsDevelopmentStorageAccount(possibleAccount))
            {
                account = null;
                return StorageAccountParseResult.EmulatorIsNotSupportedError;
            }

            account = possibleAccount;
            return StorageAccountParseResult.Success;
        }

        /// <summary>
        /// Formats an error message corresponding to the provided error code and account.
        /// </summary>
        /// <param name="error">The error code as returned by <see cref="TryParseAccount"/> method call.</param>
        /// <param name="connectionStringName">Friendly connection string name used to format error message</param>
        /// <returns>Formatted error message with details about reason of the failure and possible ways of mitigation</returns>
        public static string FormatParseAccountErrorMessage(StorageAccountParseResult error, string connectionStringName)
        {
            switch (error)
            {
                case StorageAccountParseResult.MissingOrEmptyConnectionStringError:
                    return String.Format(CultureInfo.CurrentCulture,
                        "Microsoft Azure WebJobs SDK {0} connection string is missing or empty. " +
                        "The Microsoft Azure Storage account connection string can be set in the following ways:" + Environment.NewLine +
                        "1. Set the connection string named '{1}' in the connectionStrings section of the .config file in the following format " +
                        "<add name=\"{1}\" connectionString=\"DefaultEndpointsProtocol=http|https;AccountName=NAME;AccountKey=KEY\" />, or" + Environment.NewLine +
                        "2. Set the environment variable named '{1}', or" + Environment.NewLine +
                        "3. Set corresponding property of JobHostConfiguration.",
                        connectionStringName,
                        AmbientConnectionStringProvider.GetPrefixedConnectionStringName(connectionStringName));

                case StorageAccountParseResult.MalformedConnectionStringError:
                    return String.Format(CultureInfo.CurrentCulture,
                        "Failed to validate Microsoft Azure WebJobs SDK {0} connection string. " +
                        "The Microsoft Azure Storage account connection string is not formatted " +
                        "correctly. Please visit http://msdn.microsoft.com/en-us/library/windowsazure/ee758697.aspx for " +
                        "details about configuring Microsoft Azure Storage connection strings.",
                        connectionStringName);

                case StorageAccountParseResult.EmulatorIsNotSupportedError:
                    return String.Format(CultureInfo.CurrentCulture,
                        "Failed to validate Microsoft Azure WebJobs SDK {0} account. " + 
                        "The Microsoft Azure Storage Emulator is not supported, please use a " +
                        "Microsoft Azure Storage account hosted in Microsoft Azure.",
                        connectionStringName);
            }

            Debug.Assert(false, "Unsupported case of error message!");
            return String.Empty;
        }
    }
}
