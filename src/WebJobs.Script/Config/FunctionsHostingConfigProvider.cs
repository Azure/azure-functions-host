// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Permissions;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    /// <summary>
    /// A file-based hosting configuration <see cref="FileConfigurationProvider"/>.
    /// </summary>
    public class FunctionsHostingConfigProvider : FileConfigurationProvider
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FunctionsHostingConfigProvider"/> class.
        /// </summary>
        /// <param name="source">The source settings.</param>
        public FunctionsHostingConfigProvider(FunctionsHostingConfigSource source) : base(source) { }

        /// <summary>
        /// Loads the hosting config data from a stream.
        /// </summary>
        /// <param name="stream">The stream to read.</param>
        public override void Load(Stream stream)
        {
            StreamReader reader = new StreamReader(stream);
            string text = reader.ReadToEnd();
            Data = Parse(text);
        }

        private static Dictionary<string, string> Parse(string settings)
        {
            // Expected settings: "ENABLE_FEATUREX=1,A=B,TimeOut=123"
            return string.IsNullOrEmpty(settings)
                ? new Dictionary<string, string>()
                : settings
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries))
                    .Where(a => a.Length == 2)
                    .ToDictionary(a => $"{ScriptConstants.FunctionsHostingConfigSectionName}:{a[0]}", a => a[1], StringComparer.OrdinalIgnoreCase);
        }
    }
}
