// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Configuration;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class Utility
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
            string flattenedErrors = string.Empty;
            string lastError = null;

            if (ex is AggregateException)
            {
                ex = ex.InnerException;
            }

            do
            {
                string currentError = string.Empty;
                if (!string.IsNullOrEmpty(ex.Source))
                {
                    currentError += ex.Source + ": ";
                }

                currentError += ex.Message;

                if (!currentError.EndsWith("."))
                {
                    currentError += ".";
                }

                // sometimes inner exceptions are exactly the same
                // so first check before duplicating
                if (lastError == null ||
                    string.Compare(lastError.Trim(), currentError.Trim()) != 0)
                {
                    if (flattenedErrors.Length > 0)
                    {
                        flattenedErrors += " ";
                    }
                    flattenedErrors += currentError;
                }

                lastError = currentError;
            }
            while ((ex = ex.InnerException) != null);

            return flattenedErrors;
        }

        public static string GetAppSettingOrEnvironmentValue(string name)
        {
            // first check app settings
            string value = ConfigurationManager.AppSettings[name];
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }

            // Check environment variables
            value = Environment.GetEnvironmentVariable(name);
            if (value != null)
            {
                return value;
            }

            return null;
        }
    }
}
