// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    internal class SecretsUtility
    {
        public static string GetNonDecryptableName(string secretsPath)
        {
            string timeStamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH.mm.ss.ffffff");
            if (secretsPath.EndsWith(".json"))
            {
                secretsPath = secretsPath.Substring(0, secretsPath.Length - 5);
            }
            return secretsPath + $".{ScriptConstants.Snapshot}.{timeStamp}.json";
        }
    }
}