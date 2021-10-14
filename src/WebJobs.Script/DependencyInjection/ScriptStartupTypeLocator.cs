// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.ExtensionRequirements;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.DependencyInjection
{
    /// <summary>
    /// An implementation of an <see cref="IWebJobsStartupTypeLocator"/> that locates startup types
    /// from extension registrations.
    /// </summary>
    public class ScriptStartupTypeLocator : IWebJobsStartupTypeLocator
    {
        private readonly string _rootScriptPath;
        private readonly IEnvironment _environment;
        private readonly ILogger _logger;
        private readonly IExtensionBundleManager _extensionBundleManager;
        private readonly IFunctionMetadataManager _functionMetadataManager;
        private readonly IMetricsLogger _metricsLogger;
        private readonly bool _isSelfHost;
        private readonly Lazy<IEnumerable<Type>> _startupTypes;

        private static readonly ExtensionRequirementsInfo _extensionRequirements = DependencyHelper.GetExtensionRequirements();
        private static string[] _builtinExtensionAssemblies = GetBuiltinExtensionAssemblies();

        public ScriptStartupTypeLocator(
            string rootScriptPath,
            IEnvironment environment,
            ILogger<ScriptStartupTypeLocator> logger,
            IExtensionBundleManager extensionBundleManager,
            IFunctionMetadataManager functionMetadataManager,
            IMetricsLogger metricsLogger,
            bool isSelfHost = false)
        {
            _rootScriptPath = rootScriptPath ?? throw new ArgumentNullException(nameof(rootScriptPath));
            _environment = environment;
            _extensionBundleManager = extensionBundleManager ?? throw new ArgumentNullException(nameof(extensionBundleManager));
            _logger = logger;
            _functionMetadataManager = functionMetadataManager;
            _metricsLogger = metricsLogger;
            _isSelfHost = isSelfHost;
            _startupTypes = new Lazy<IEnumerable<Type>>(() => GetExtensionsStartupTypesAsync().ConfigureAwait(false).GetAwaiter().GetResult());
        }

        private static string[] GetBuiltinExtensionAssemblies()
        {
            return new[]
            {
                typeof(WebJobs.Extensions.Http.HttpWebJobsStartup).Assembly.GetName().Name,
                typeof(WebJobs.Extensions.ExtensionsWebJobsStartup).Assembly.GetName().Name
            };
        }

        public Type[] GetStartupTypes()
        {
            return _startupTypes.Value
                .Distinct(new TypeNameEqualityComparer())
                .ToArray();
        }

        internal bool HasExternalConfigurationStartups() => _startupTypes.Value.Any(p => typeof(IWebJobsConfigurationStartup).IsAssignableFrom(p));

        public async Task<IEnumerable<Type>> GetExtensionsStartupTypesAsync()
        {
            string extensionsMetadataPath;
            var functionMetadataCollection = _functionMetadataManager.GetFunctionMetadata(forceRefresh: true, includeCustomProviders: false);
            HashSet<string> bindingsSet = null;
            var bundleConfigured = _extensionBundleManager.IsExtensionBundleConfigured();
            bool isPrecompiledFunctionApp = false;

            if (bundleConfigured)
            {
                ExtensionBundleDetails bundleDetails = await _extensionBundleManager.GetExtensionBundleDetails();
                ValidateBundleRequirements(bundleDetails);

                bindingsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Generate a Hashset of all the binding types used in the function app
                foreach (var functionMetadata in functionMetadataCollection)
                {
                    foreach (var binding in functionMetadata.Bindings)
                    {
                        bindingsSet.Add(binding.Type);
                    }
                    isPrecompiledFunctionApp = isPrecompiledFunctionApp || functionMetadata.Language == DotNetScriptTypes.DotNetAssembly;
                }
            }

            bool isLegacyExtensionBundle = _extensionBundleManager.IsLegacyExtensionBundle();

            if (SystemEnvironment.Instance.IsPlaceholderModeEnabled())
            {
                // Do not move this.
                // Calling this log statement in the placeholder mode to avoid jitting during specializtion
                _logger.ScriptStartNotLoadingExtensionBundle("WARMUP_LOG_ONLY", bundleConfigured, isPrecompiledFunctionApp, isLegacyExtensionBundle);
            }

            string baseProbingPath = null;

            if (bundleConfigured && (!isPrecompiledFunctionApp || _extensionBundleManager.IsLegacyExtensionBundle()))
            {
                extensionsMetadataPath = await _extensionBundleManager.GetExtensionBundleBinPathAsync();
                if (string.IsNullOrEmpty(extensionsMetadataPath))
                {
                    _logger.ScriptStartUpErrorLoadingExtensionBundle();
                    return Array.Empty<Type>();
                }

                _logger.ScriptStartUpLoadingExtensionBundle(extensionsMetadataPath);
            }
            else
            {
                extensionsMetadataPath = Path.Combine(_rootScriptPath, "bin");

                // Verify if the file exists and apply fallback paths
                // The fallback order is:
                //   1 - Script root
                //       - If the system folder exists with metadata file at the root, use that as the base probing path
                //   2 - System folder
                if (!File.Exists(Path.Combine(extensionsMetadataPath, ScriptConstants.ExtensionsMetadataFileName)))
                {
                    string systemPath = Path.Combine(_rootScriptPath, ScriptConstants.AzureFunctionsSystemDirectoryName);

                    if (File.Exists(Path.Combine(_rootScriptPath, ScriptConstants.ExtensionsMetadataFileName)))
                    {
                        // As a fallback, allow extensions.json in the root path.
                        extensionsMetadataPath = _rootScriptPath;

                        // If the system path exists, that should take precedence as the base probing path
                        if (Directory.Exists(systemPath))
                        {
                            baseProbingPath = systemPath;
                        }
                    }
                    else if (File.Exists(Path.Combine(systemPath, ScriptConstants.ExtensionsMetadataFileName)))
                    {
                        extensionsMetadataPath = systemPath;
                    }
                }

                _logger.ScriptStartNotLoadingExtensionBundle(extensionsMetadataPath, bundleConfigured, isPrecompiledFunctionApp, isLegacyExtensionBundle);
            }

            baseProbingPath ??= extensionsMetadataPath;
            _logger.ScriptStartupResettingLoadContextWithBasePath(baseProbingPath);

            // Reset the load context using the resolved extensions path
            FunctionAssemblyLoadContext.ResetSharedContext(baseProbingPath);

            string metadataFilePath = Path.Combine(extensionsMetadataPath, ScriptConstants.ExtensionsMetadataFileName);

            // parse the extensions file to get declared startup extensions
            ExtensionReference[] extensionItems = ParseExtensions(metadataFilePath);

            var startupTypes = new List<Type>();

            foreach (var extensionItem in extensionItems)
            {
                if (!bundleConfigured || IsValidBindingMatch(extensionItem, bindingsSet, _environment))
                {
                    string startupExtensionName = extensionItem.Name ?? extensionItem.TypeName;
                    _logger.ScriptStartUpLoadingStartUpExtension(startupExtensionName);

                    // load the Type for each startup extension into the function assembly load context
                    Type extensionType = Type.GetType(extensionItem.TypeName,
                        assemblyName =>
                        {
                            if (_builtinExtensionAssemblies.Contains(assemblyName.Name, StringComparer.OrdinalIgnoreCase))
                            {
                                _logger.ScriptStartUpBelongExtension(extensionItem.TypeName);
                                return null;
                            }

                            string path = extensionItem.HintPath;
                            if (string.IsNullOrEmpty(path))
                            {
                                path = assemblyName.Name + ".dll";
                            }

                            var hintUri = new Uri(path, UriKind.RelativeOrAbsolute);
                            if (!hintUri.IsAbsoluteUri)
                            {
                                path = Path.Combine(extensionsMetadataPath, path);
                            }

                            if (File.Exists(path))
                            {
                                return FunctionAssemblyLoadContext.Shared.LoadFromAssemblyPath(path, true);
                            }

                            return null;
                        },
                        (assembly, typeName, ignoreCase) =>
                        {
                            _logger.ScriptStartUpLoadedExtension(startupExtensionName, assembly.GetName().Version.ToString());
                            return assembly?.GetType(typeName, false, ignoreCase);
                        }, false, true);

                    if (extensionType == null)
                    {
                        _logger.ScriptStartUpUnableToLoadExtension(startupExtensionName, extensionItem.TypeName);
                        continue;
                    }

                    if (!typeof(IWebJobsStartup).IsAssignableFrom(extensionType) && !typeof(IWebJobsConfigurationStartup).IsAssignableFrom(extensionType))
                    {
                        _logger.ScriptStartUpTypeIsNotValid(extensionItem.TypeName, nameof(IWebJobsStartup), nameof(IWebJobsConfigurationStartup));
                        continue;
                    }

                    startupTypes.Add(extensionType);
                }
            }

            ValidateExtensionRequirements(startupTypes);
            ValidateApplicationInsightsConfig(startupTypes, _isSelfHost, IsApplicationInsightsSettingPresent(_environment), bundleConfigured, _logger);

            return startupTypes;
        }

        private ExtensionReference[] ParseExtensions(string metadataFilePath)
        {
            using (_metricsLogger.LatencyEvent(MetricEventNames.ParseExtensions))
            {
                if (!File.Exists(metadataFilePath))
                {
                    return Array.Empty<ExtensionReference>();
                }

                try
                {
                    var extensionMetadata = JObject.Parse(File.ReadAllText(metadataFilePath));

                    var extensionItems = extensionMetadata["extensions"]?.ToObject<List<ExtensionReference>>();
                    if (extensionItems == null)
                    {
                        _logger.ScriptStartUpUnableParseMetadataMissingProperty(metadataFilePath);
                        return Array.Empty<ExtensionReference>();
                    }

                    return extensionItems.ToArray();
                }
                catch (JsonReaderException exc)
                {
                    _logger.ScriptStartUpUnableParseMetadata(exc, metadataFilePath);

                    return Array.Empty<ExtensionReference>();
                }
            }
        }

        private void ValidateBundleRequirements(ExtensionBundleDetails bundleDetails)
        {
            if (_extensionRequirements.BundleRequirementsByBundleId.TryGetValue(bundleDetails.Id, out BundleRequirement requirement))
            {
                var bundleVersion = new Version(bundleDetails.Version);
                var minimumVersion = new Version(requirement.MinimumVersion);

                if (bundleVersion < minimumVersion)
                {
                    _logger.MinimumBundleVersionNotSatisfied(bundleDetails.Id, bundleDetails.Version, requirement.MinimumVersion);
                    throw new HostInitializationException($"Referenced bundle {bundleDetails.Id} of version {bundleDetails.Version} does not meet the required minimum version of {requirement.MinimumVersion}. Update your extension bundle reference in host.json to reference {requirement.MinimumVersion} or later. For more information see https://aka.ms/func-min-bundle-versions.");
                }
            }
        }

        private void ValidateExtensionRequirements(List<Type> startupTypes)
        {
            var errors = new List<string>();

            void CollectError(Type extensionType, Version minimumVersion, ExtensionStartupTypeRequirement requirement)
            {
                _logger.MinimumExtensionVersionNotSatisfied(extensionType.Name, extensionType.Assembly.FullName, minimumVersion, requirement.PackageName, requirement.MinimumPackageVersion);
                string requirementNotMetError = $"ExtensionStartupType {extensionType.Name} from assembly '{extensionType.Assembly.FullName}' does not meet the required minimum version of {minimumVersion}. Update your NuGet package reference for {requirement.PackageName} to {requirement.MinimumPackageVersion} or later.";
                errors.Add(requirementNotMetError);
            }

            foreach (var extensionType in startupTypes)
            {
                if (_extensionRequirements.ExtensionRequirementsByStartupType.TryGetValue(extensionType.Name, out ExtensionStartupTypeRequirement requirement))
                {
                    Version minimumAssemblyVersion = new Version(requirement.MinimumAssemblyVersion);

                    AssemblyName assemblyName = extensionType.Assembly.GetName();
                    Version extensionAssemblyVersion = assemblyName.Version;
                    string extensionAssemblySimpleName = assemblyName.Name;

                    if (extensionAssemblySimpleName != requirement.AssemblyName)
                    {
                        // We recognized this type, but it did not come from the assembly that we expect, skipping validation
                        continue;
                    }

                    if (requirement.MinimumAssemblyFileVersion != null && !string.IsNullOrEmpty(extensionType.Assembly.Location))
                    {
                        Version minimumAssemblyFileVersion = new Version(requirement.MinimumAssemblyFileVersion);

                        try
                        {
                            Version extensionAssemblyFileVersion = new Version(System.Diagnostics.FileVersionInfo.GetVersionInfo(extensionType.Assembly.Location).FileVersion);
                            if (extensionAssemblyFileVersion < minimumAssemblyFileVersion)
                            {
                                CollectError(extensionType, minimumAssemblyFileVersion, requirement);
                                continue;
                            }
                        }
                        catch (FormatException)
                        {
                            // We failed to parse the assembly file version as a Version. We can't do a version check - give up
                            continue;
                        }
                    }

                    if (extensionAssemblyVersion < minimumAssemblyVersion)
                    {
                        CollectError(extensionType, minimumAssemblyVersion, requirement);
                    }
                }
            }

            if (errors.Count > 0)
            {
                var builder = new System.Text.StringBuilder();
                builder.AppendLine("One or more loaded extensions do not meet the minimum requirements. For more information see https://aka.ms/func-min-extension-versions.");
                foreach (string e in errors)
                {
                    builder.AppendLine(e);
                }

                throw new HostInitializationException(builder.ToString());
            }
        }

        internal static bool IsValidBindingMatch(ExtensionReference extension, HashSet<string> bindingsSet, IEnvironment environment)
        {
            if (extension.Bindings.Count == 0)
            {
                return true;
            }

            // Application Insights uses a special binding value to ensure it's installed based on its App Setting
            if (extension.Bindings.Any(b => b.Equals("_")))
            {
                return IsApplicationInsightsSettingPresent(environment);
            }

            return extension.Bindings.Intersect(bindingsSet, StringComparer.OrdinalIgnoreCase).Any();
        }

        internal static void ValidateApplicationInsightsConfig(List<Type> startupTypes, bool isSelfHost, bool isApplicationInsightsSettingPresent, bool isExtensionBundleConfigured, ILogger logger)
        {
            if (isSelfHost)
            {
                logger.LogWarning("In order to use Application Insights in Azure Functions V4 and above, please install the Application Insights Extension. " +
                                  "See https://aka.ms/func-applicationinsights-extension for more details.");
            }
            else if (!isExtensionBundleConfigured)
            {
                bool applicationInsightsInstalled = startupTypes.Exists(t => t.Name.Equals("ApplicationInsightsWebJobsStartup"));
                if (applicationInsightsInstalled && !isApplicationInsightsSettingPresent)
                {
                    throw new HostInitializationException($"The Application Insights Extension is installed but is not properly configured. " +
                                                          $"Please define the \"{EnvironmentSettingNames.AppInsightsConnectionString}\" app setting.");
                }
                else if (!applicationInsightsInstalled && isApplicationInsightsSettingPresent)
                {
                    // this could break extension bundle apps depending on when application insights extension is
                    // released in bundles, so ignore them.
                    throw new HostInitializationException($"{EnvironmentSettingNames.AppInsightsConnectionString} or {EnvironmentSettingNames.AppInsightsInstrumentationKey} " +
                                                          $"is defined but the Application Insights Extension is not installed. Please install the Application Insights Extension. " +
                                                          $"See https://aka.ms/func-applicationinsights-extension for more details.");
                }
            }
        }

        internal static bool IsApplicationInsightsSettingPresent(IEnvironment environment)
        {
            return !string.IsNullOrEmpty(environment.GetEnvironmentVariable(EnvironmentSettingNames.AppInsightsConnectionString))
                      || !string.IsNullOrEmpty(environment.GetEnvironmentVariable(EnvironmentSettingNames.AppInsightsInstrumentationKey));
        }

        private class TypeNameEqualityComparer : IEqualityComparer<Type>
        {
            public bool Equals(Type x, Type y)
            {
                if (x == y)
                {
                    return true;
                }

                if (x == null || y == null)
                {
                    return false;
                }

                return string.Equals(x.FullName, y.FullName, StringComparison.Ordinal);
            }

            public int GetHashCode(Type obj)
            {
                return obj?.FullName?.GetHashCode() ?? 0;
            }
        }
    }
}
