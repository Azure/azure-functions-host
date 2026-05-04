// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd
{
    /// <summary>
    /// Regression tests for the Logic Apps (workflowapp) bundle-shipped worker scenario tied to
    /// PR #11582 (LanguageWorkerOptions cache invalidation rework). These tests verify that worker
    /// directories contributed to <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> by
    /// an extension bundle's <see cref="Microsoft.Azure.WebJobs.Hosting.IWebJobsConfigurationStartup"/>
    /// end up reflected in <see cref="WorkerConfigurationResolverOptions.WorkerDescriptionOverrides"/>
    /// after the JobHost is built.
    /// </summary>
    /// <remarks>
    /// This is the Logic Apps shape: <c>APP_KIND=workflowapp</c>, dedicated mode only (no
    /// specialization), no worker indexing. Logic Apps relies on the Workflows bundle's
    /// <c>IWebJobsConfigurationStartup</c> to add <c>languageWorkers:&lt;name&gt;:workerDirectory</c>
    /// entries; the host must recompute its worker options after that contribution lands.
    /// <para>
    /// The test stages a fake bundle layout on disk and points the real <c>ExtensionBundleManager</c>
    /// at it via <c>extensionBundle:downloadPath</c>. The bundle DLL
    /// (<c>WorkflowAppTestBundle.dll</c>) implements <c>IWebJobsConfigurationStartup</c> and adds a
    /// known <c>languageWorkers:workflow-test-worker:workerDirectory</c> entry. The test then asserts
    /// that this entry is visible to <see cref="WorkerConfigurationResolverOptions"/> after the host
    /// has started.
    /// </para>
    /// <para>
    /// Pre-PR-#11582 this test passes: <c>HostBuiltChangeTokenSource&lt;LanguageWorkerOptions&gt;.TriggerChange()</c>
    /// fires unconditionally at the end of host start, which invalidates the worker-options cache so
    /// that the next read sees the bundle's contribution. Post-PR-#11582 this test fails: the bundle
    /// branch in <c>ScriptStartupTypeLocator</c> calls <c>InvalidateCacheForBundles()</c> (a no-op on
    /// first run) and never arms <c>EnableInvalidationForNextBuild</c>, so the cache is never
    /// invalidated and the bundle worker never appears in
    /// <see cref="WorkerConfigurationResolverOptions.WorkerDescriptionOverrides"/>.
    /// </para>
    /// </remarks>
    [Trait(TestTraits.Group, TestTraits.NonE2EWebHost)]
    public class WorkflowAppEndToEndTests
    {
        private const string BundleId = "Microsoft.Azure.Functions.ExtensionBundle.Workflows";
        private const string BundleVersion = "1.0.0";
        private const string BundleWorkerName = "workflow-test-worker";
        private const string BundleStartupTypeName = "WorkflowAppTestBundle.WorkflowAppTestBundleStartup, WorkflowAppTestBundle";
        private const string BundleAssemblyFileName = "WorkflowAppTestBundle.dll";

        // Mirrors the path convention used by SpecializationE2ETests for sibling test projects
        // built into out/bin/<ProjectName>/<BuildConfig>/.
        private static readonly string s_bundleAssemblyPath = Path.GetFullPath(
            Path.Combine("..", "..", "WorkflowAppTestBundle", TestHelpers.BuildConfig, BundleAssemblyFileName));

        // Script root copied into test output via the integration project's TestScripts glob.
        private static readonly string s_scriptRootPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "TestScripts", "WorkflowAppFunctionApp"));

        [Fact]
        public async Task WorkflowApp_BundleShippedWorkerDirectory_AppearsInWorkerConfigurationResolverOptions()
        {
            Assert.True(File.Exists(s_bundleAssemblyPath),
                $"Test bundle assembly not found at '{s_bundleAssemblyPath}'. Ensure the WorkflowAppTestBundle project has been built.");
            Assert.True(Directory.Exists(s_scriptRootPath),
                $"Test script root not found at '{s_scriptRootPath}'. Ensure the WorkflowAppFunctionApp TestScripts content has been copied to test output.");

            string bundleRoot = StageFakeWorkflowBundle();

            // Scope all env-var changes so prior values are restored on dispose. This includes the
            // AppService/Container hosting probes used by ExtensionBundleConfigurationHelper to
            // override DownloadPath - if those leak in from the host process (e.g., a CI agent that
            // has WEBSITE_INSTANCE_ID set), our explicit downloadPath override is silently
            // clobbered and the test fails for the wrong reason.
            var envVars = new Dictionary<string, string>
            {
                [EnvironmentSettingNames.AppKind] = ScriptConstants.WorkFlowAppKind,
                [EnvironmentSettingNames.FunctionWorkerRuntime] = "node",
                [$"{ConfigurationSectionNames.JobHost}__{ConfigurationSectionNames.ExtensionBundle}__downloadPath"] = bundleRoot,
                [EnvironmentSettingNames.AzureWebsiteHomePath] = null,
                [EnvironmentSettingNames.AzureWebsiteInstanceId] = null,
                [EnvironmentSettingNames.ContainerName] = null,
            };

            try
            {
                using var envScope = new TestScopedEnvironmentVariable(envVars);
                using var host = new TestFunctionHost(s_scriptRootPath);

                // Sanity check: the bundle's IWebJobsConfigurationStartup must have run and added
                // its languageWorkers:<name>:workerDirectory entry to the JobHost-scope
                // IConfiguration. If this fails, the test is exercising the wrong path (e.g., the
                // bundle was not located/loaded at all) rather than the cache-invalidation
                // regression.
                string configKey = $"languageWorkers:{BundleWorkerName}:workerDirectory";
                var jobHostConfig = host.JobHostServices.GetRequiredService<IConfiguration>();
                Assert.True(
                    !string.IsNullOrEmpty(jobHostConfig[configKey]),
                    $"Bundle's IWebJobsConfigurationStartup did not contribute '{configKey}' to the " +
                    $"JobHost IConfiguration. The fake Workflows bundle was not loaded as expected. " +
                    $"Logs:\n{host.GetLog()}");

                var resolverOptions = host.WebHostServices
                    .GetRequiredService<IOptionsMonitor<WorkerConfigurationResolverOptions>>()
                    .CurrentValue;

                Assert.NotNull(resolverOptions.WorkerDescriptionOverrides);

                Assert.True(
                    resolverOptions.WorkerDescriptionOverrides.ContainsKey(BundleWorkerName),
                    $"Expected bundle-shipped worker '{BundleWorkerName}' to be present in " +
                    $"{nameof(WorkerConfigurationResolverOptions)}.{nameof(WorkerConfigurationResolverOptions.WorkerDescriptionOverrides)} " +
                    $"after the JobHost was built. Present keys: " +
                    $"[{string.Join(", ", resolverOptions.WorkerDescriptionOverrides.Keys)}]. " +
                    "This indicates worker options were not recomputed after the bundle's IWebJobsConfigurationStartup ran. " +
                    $"Logs:\n{host.GetLog()}");
            }
            finally
            {
                TryDelete(bundleRoot);
            }

            // Allow async patterns/log flushing without a synchronous-only test signature.
            await Task.CompletedTask;
        }

        /// <summary>
        /// Lays out a fake extension-bundle directory tree that the real
        /// <c>ExtensionBundleManager.TryLocateExtensionBundle</c> can probe:
        /// <code>
        /// &lt;tempDir&gt;/
        ///   1.0.0/
        ///     bundle.json
        ///     bin/
        ///       extensions.json
        ///       WorkflowAppTestBundle.dll
        /// </code>
        /// The bundle DLL is copied from the WorkflowAppTestBundle test project's build output. Its
        /// transitive dependencies (e.g., Microsoft.Azure.WebJobs.Host) are resolved by the host's
        /// already-loaded default AssemblyLoadContext when the type is loaded by the locator.
        /// </summary>
        private static string StageFakeWorkflowBundle()
        {
            string bundleRoot = Path.Combine(Path.GetTempPath(), "WorkflowAppEndToEndTests", Guid.NewGuid().ToString("N"));
            string versionDir = Path.Combine(bundleRoot, BundleVersion);
            string binDir = Path.Combine(versionDir, "bin");

            Directory.CreateDirectory(binDir);

            // bundle.json: minimal metadata file. ExtensionBundleManager.TryLocateExtensionBundle
            // verifies its existence; the version is derived from the parent directory name.
            File.WriteAllText(
                Path.Combine(versionDir, ScriptConstants.ExtensionBundleMetadataFile),
                $"{{\"id\":\"{BundleId}\",\"version\":\"{BundleVersion}\",\"majorVersion\":1}}");

            // extensions.json: declares our IWebJobsConfigurationStartup. Empty bindings mirrors the
            // real Workflows bundle invariant (extensions don't declare bindings), letting the
            // locator's bindingsSet filter pass through unconditionally.
            File.WriteAllText(
                Path.Combine(binDir, ScriptConstants.ExtensionsMetadataFileName),
                $@"{{
  ""extensions"": [
    {{
      ""name"": ""WorkflowAppTestBundle"",
      ""typeName"": ""{BundleStartupTypeName}"",
      ""hintPath"": ""{BundleAssemblyFileName}"",
      ""bindings"": []
    }}
  ]
}}");

            File.Copy(s_bundleAssemblyPath, Path.Combine(binDir, BundleAssemblyFileName));

            return bundleRoot;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (path is not null && Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
                // best effort - test cleanup
            }
        }
    }
}
