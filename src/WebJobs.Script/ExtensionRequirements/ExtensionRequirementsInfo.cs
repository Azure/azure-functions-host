// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Script.ExtensionRequirements
{
    internal sealed class ExtensionRequirementsInfo
    {
        private Dictionary<string, BundleRequirement> _bundleRequirementsById;
        private Dictionary<string, ExtensionStartupTypeRequirement> _extensionRequirementsByStartupType;
        private BundleRequirement[] _bundles = [];
        private ExtensionStartupTypeRequirement[] _types = [];

        public BundleRequirement[] Bundles
        {
            get => _bundles;
            set
            {
                _bundles = value ?? [];
                _bundleRequirementsById = null;
            }
        }

        public ExtensionStartupTypeRequirement[] Types
        {
            get => _types;
            set
            {
                _types = value ?? [];
                _extensionRequirementsByStartupType = null;
            }
        }

        public Dictionary<string, BundleRequirement> BundleRequirementsByBundleId =>
            _bundleRequirementsById ??= Bundles.ToDictionary(b => b.Id, StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, ExtensionStartupTypeRequirement> ExtensionRequirementsByStartupType =>
            _extensionRequirementsByStartupType ??= Types.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }
}
