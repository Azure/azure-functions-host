﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Kudu
{
    public interface IEnvironment
    {
        string RootPath { get; }                // e.g. /
        string SiteRootPath { get; }            // e.g. /site
        string ApplicationLogFilesPath { get; } // e.g. /logfiles/application
        string DataPath { get; }                // e.g. /data
        string FunctionsPath { get; }           // e.g. /site/wwwroot
        string AppBaseUrlPrefix { get; }        // e.g. siteName.azurewebsites.net
    }
}