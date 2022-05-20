// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    // HostPropertycondition checks if host match the expected output for properties such as Sku, Platform, HostVersion
    internal class HostPropertyCondition : IWorkerProfileCondition
    {
        private readonly ILogger _logger;
        private readonly ISystemRuntimeInformation _systemRuntimeInformation;
        private readonly string _name;
        private readonly string _expression;
        private Regex _regex;

        public HostPropertyCondition(ILogger logger, ISystemRuntimeInformation systemRuntimeInformation, WorkerProfileConditionDescriptor descriptor)
        {
            if (descriptor is null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemRuntimeInformation = systemRuntimeInformation ?? throw new ArgumentNullException(nameof(systemRuntimeInformation));

            descriptor.Properties.TryGetValue(WorkerConstants.WorkerDescriptionProfileConditionName, out _name);
            descriptor.Properties.TryGetValue(WorkerConstants.WorkerDescriptionProfileConditionExpression, out _expression);

            Validate();
        }

        public enum HostProperty
        {
            Sku,
            Platform,
            HostVersion
        }

        public string Name => _name;

        public string Expression => _expression;

        /// <inheritdoc />
        public bool Evaluate()
        {
            Enum.TryParse(Name, out HostProperty hostPropertyName);

            string value = hostPropertyName switch
            {
                HostProperty.Sku => ScriptSettingsManager.Instance.GetSetting(EnvironmentSettingNames.AzureWebsiteSku),
                HostProperty.Platform => _systemRuntimeInformation.GetOSPlatform().ToString(),
                HostProperty.HostVersion => ScriptHost.Version,
                _ => null
            };

            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            _logger.LogDebug($"Evaluating HostPropertyCondition with value: {value} and expression {Expression}");

            return _regex.IsMatch(value);
        }

        // Validates if condition parametrs meet expected values, fail if they don't
        internal void Validate()
        {
            if (string.IsNullOrEmpty(Name))
            {
               throw new ValidationException($"HostPropertyCondition {nameof(Name)} cannot be empty.");
            }

            if (!Enum.GetNames(typeof(HostProperty)).Any(x => x.ToLower().Contains(Name)))
            {
               throw new ValidationException($"HostPropertyCondition {nameof(Name)} is not a valid host property name.");
            }

            if (string.IsNullOrEmpty(Expression))
            {
               throw new ValidationException($"HostPropertyCondition {nameof(Expression)} cannot be empty.");
            }

            try
            {
                _regex = new Regex(Expression);
            }
            catch
            {
              throw new ValidationException($"HostPropertyCondition {nameof(Expression)} must be a valid regular expression.");
            }
        }
    }
}