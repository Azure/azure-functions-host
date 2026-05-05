// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Extensions.Configuration;

namespace WorkflowAppTestBundle
{
    /// <summary>
    /// Mimics the contribution of an <c>IWebJobsConfigurationStartup</c> shipped inside the real
    /// Logic Apps Workflows extension bundle: adds a <c>languageWorkers:&lt;name&gt;:workerDirectory</c>
    /// entry to the JobHost-scope <see cref="IConfigurationBuilder"/>. The expectation is that the
    /// host recomputes <c>WorkerConfigurationResolverOptions</c>/<c>LanguageWorkerOptions</c> after
    /// this startup runs so the bundle-shipped worker becomes visible to the runtime.
    /// </summary>
    public class WorkflowAppTestBundleStartup : IWebJobsConfigurationStartup
    {
        public const string BundleWorkerName = "workflow-test-worker";

        // Path is never opened; tests only assert that the entry round-trips into
        // WorkerConfigurationResolverOptions. Use Path.Combine off the temp directory so the value
        // is path-portable across Windows and *nix.
        public static readonly string BundleWorkerDirectory = Path.Combine(Path.GetTempPath(), "stub", BundleWorkerName);

        public void Configure(WebJobsBuilderContext context, IWebJobsConfigurationBuilder builder)
        {
            builder.ConfigurationBuilder.AddInMemoryCollection(new Dictionary<string, string>
            {
                [$"languageWorkers:{BundleWorkerName}:workerDirectory"] = BundleWorkerDirectory,
            });
        }
    }
}
