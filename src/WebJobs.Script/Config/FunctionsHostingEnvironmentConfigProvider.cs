﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Permissions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    public class FunctionsHostingEnvironmentConfigProvider : JsonConfigurationProvider
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FunctionsHostingEnvironmentConfigProvider"/> class.
        /// </summary>
        /// <param name="source">The source settings.</param>
        public FunctionsHostingEnvironmentConfigProvider(FunctionsHostingEnvironmentConfigSource source) : base(source) { }

        /// <summary>
        /// Loads the hosting config data from a stream.
        /// </summary>
        /// <param name="stream">The stream to read.</param>
        public override void Load(Stream stream)
        {
            base.Load(stream);

            var updatedDataDictionay = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in Data)
            {
                updatedDataDictionay.Add($"{ScriptConstants.FunctionsHostingEnvironmentConfigSectionName}:{kvp.Key}", kvp.Value);
            }

            Data = updatedDataDictionay;
        }
    }
}
