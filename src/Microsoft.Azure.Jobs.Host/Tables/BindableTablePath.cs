// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Bindings;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal static class BindableTablePath
    {
        public static IBindableTablePath Create(string tableNamePattern)
        {
            List<string> parameterNames = new List<string>();

            BindingDataPath.AddParameterNames(tableNamePattern, parameterNames);

            if (parameterNames.Count > 0)
            {
                return new ParameterizedTablePath(tableNamePattern, parameterNames);
            }

            TableClient.ValidateAzureTableName(tableNamePattern);
            return new BoundTablePath(tableNamePattern);
        }
    }
}
