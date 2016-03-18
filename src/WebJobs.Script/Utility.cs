// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script
{
    internal static class Utility
    {
        public static string GetFunctionShortName(string functionName)
        {
            int idx = functionName.LastIndexOf('.');
            if (idx > 0)
            {
                functionName = functionName.Substring(idx + 1);
            }

            return functionName;
        }

        public static string FlattenException(Exception ex)
        {
            string formattedError = ex.Message;

            while ((ex = ex.InnerException) != null)
            {
                formattedError += ". " + ex.Message;
            }

            return formattedError;
        }
    }
}
