﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Configuration;
using System.Text;

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
            StringBuilder flattenedErrorsBuilder = new StringBuilder();
            string lastError = null;

            if (ex is AggregateException)
            {
                ex = ex.InnerException;
            }

            do
            {
                StringBuilder currentErrorBuilder = new StringBuilder();
                if (!string.IsNullOrEmpty(ex.Source))
                {
                    currentErrorBuilder.AppendFormat("{0}: ", ex.Source);
                }

                currentErrorBuilder.Append(ex.Message);

                if (!ex.Message.EndsWith("."))
                {
                    currentErrorBuilder.Append(".");
                }

                // sometimes inner exceptions are exactly the same
                // so first check before duplicating
                string currentError = currentErrorBuilder.ToString();
                if (lastError == null ||
                    string.Compare(lastError.Trim(), currentError.Trim()) != 0)
                {
                    if (flattenedErrorsBuilder.Length > 0)
                    {
                        flattenedErrorsBuilder.Append(" ");
                    }
                    flattenedErrorsBuilder.Append(currentError);
                }

                lastError = currentError;
            }
            while ((ex = ex.InnerException) != null);

            return flattenedErrorsBuilder.ToString();
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
