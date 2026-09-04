// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Conditions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.ExtensionBundle
{
    /// <summary>
    /// Reads a bundle's metadata, parses its requirements, and evaluates them using the shared
    /// condition infrastructure. Missing / empty / unreadable requirements evaluate to true
    /// (backward-compatible: bundles without requirements load unconditionally).
    /// </summary>
    internal sealed class BundleRequirementsEvaluator
    {
        private readonly IConditionProvider _conditionProvider;
        private readonly ILogger _logger;

        public BundleRequirementsEvaluator(IConditionProvider conditionProvider, ILogger logger)
        {
            _conditionProvider = conditionProvider ?? throw new ArgumentNullException(nameof(conditionProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Loads <paramref name="bundleJsonPath"/> and evaluates its requirements.
        /// Returns true when the bundle is eligible to load.
        /// </summary>
        public bool EvaluateFromFile(string bundleJsonPath, string bundleIdForLog = null, string versionForLog = null)
        {
            if (string.IsNullOrEmpty(bundleJsonPath) || !File.Exists(bundleJsonPath))
            {
                // Caller is responsible for determining whether a missing bundle.json means the bundle
                // is invalid. For requirements evaluation alone, "no bundle.json" means "nothing to
                // evaluate against" → load unconditionally.
                return true;
            }

            ExtensionBundleMetadata metadata;
            try
            {
                using var stream = File.OpenRead(bundleJsonPath);
                metadata = JsonSerializer.Deserialize<ExtensionBundleMetadata>(stream, SerializerOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Malformed bundle.json at '{path}'; requirements cannot be evaluated. Bundle will load unconditionally.", bundleJsonPath);
                return true;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Unable to read bundle.json at '{path}'; requirements cannot be evaluated. Bundle will load unconditionally.", bundleJsonPath);
                return true;
            }

            return Evaluate(metadata, bundleIdForLog, versionForLog);
        }

        /// <summary>
        /// Deserializes <paramref name="stream"/> as <see cref="ExtensionBundleMetadata"/> and
        /// evaluates its requirements. Malformed / IO-error streams are treated as "no requirements"
        /// (returns true) to preserve backward-compatible behavior. Used for CDN-fetched bundle.json.
        /// </summary>
        public async Task<bool> EvaluateFromStreamAsync(Stream stream, string bundleIdForLog = null, string versionForLog = null)
        {
            if (stream == null)
            {
                return true;
            }

            ExtensionBundleMetadata metadata;
            try
            {
                metadata = await JsonSerializer.DeserializeAsync<ExtensionBundleMetadata>(stream, SerializerOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Malformed bundle.json stream for '{id}' v{version}; assuming no requirements.", bundleIdForLog ?? "(unknown)", versionForLog ?? "(unknown)");
                return true;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "IO error reading bundle.json stream for '{id}' v{version}; assuming no requirements.", bundleIdForLog ?? "(unknown)", versionForLog ?? "(unknown)");
                return true;
            }

            return Evaluate(metadata, bundleIdForLog, versionForLog);
        }

        /// <summary>
        /// Evaluates the requirements section of the given metadata.
        /// </summary>
        public bool Evaluate(ExtensionBundleMetadata metadata, string bundleIdForLog = null, string versionForLog = null)
        {
            if (metadata?.Requirements == null || metadata.Requirements.Count == 0)
            {
                return true;
            }

            var id = bundleIdForLog ?? metadata.Id ?? "(unknown)";
            var version = versionForLog ?? metadata.Version ?? "(unknown)";

            _logger.LogDebug("Evaluating requirements for bundle '{id}' v{version} ({count} conditions).", id, version, metadata.Requirements.Count);

            var conditions = new List<ICondition>(metadata.Requirements.Count);
            for (int i = 0; i < metadata.Requirements.Count; i++)
            {
                var descriptor = metadata.Requirements[i];
                if (descriptor == null)
                {
                    _logger.LogWarning("Bundle '{id}' v{version}: requirement [{index}] is null; treating as FalseCondition.", id, version, i);
                    conditions.Add(new FalseCondition());
                    continue;
                }

                _logger.LogDebug("Evaluating condition [{index}] type='{type}'.", i, descriptor.Type);

                if (!_conditionProvider.TryCreateCondition(descriptor, out var condition))
                {
                    _logger.LogWarning("Bundle '{id}' v{version}: unable to create condition for type '{type}'; treating as FalseCondition.", id, version, descriptor.Type);
                    condition = new FalseCondition();
                }

                conditions.Add(condition);
            }

            bool result = ConditionEvaluator.EvaluateAll(conditions, _logger);
            if (result)
            {
                _logger.LogInformation("Bundle '{id}' v{version}: all {count} requirements satisfied.", id, version, conditions.Count);
            }
            else
            {
                _logger.LogInformation("Bundle '{id}' v{version} skipped: at least one requirement was not met.", id, version);
            }

            return result;
        }

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
    }
}
