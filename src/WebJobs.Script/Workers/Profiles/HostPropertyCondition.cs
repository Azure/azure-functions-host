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

        public enum ConditionHostPropertyName
        {
            Sku,
            Platform,
            HostVersion
        }

        public string Name => _name;

        public string Expression => _expression;

        public bool Evaluate()
        {
            string value = string.Empty;
            Enum.TryParse(Name, out ConditionHostPropertyName hostPropertyName);
            switch (hostPropertyName)
            {
                case ConditionHostPropertyName.Sku:
                    value = ScriptSettingsManager.Instance.GetSetting(EnvironmentSettingNames.AzureWebsiteSku);
                    break;
                case ConditionHostPropertyName.Platform:
                    value = _systemRuntimeInformation.GetOSPlatform().ToString();
                    break;
                case ConditionHostPropertyName.HostVersion:
                    value = ScriptHost.Version;
                    break;
                default:
                    break;
            }

            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            _logger.LogDebug($"Evaluating HostPropertyCondition with value: {value} and expression {Expression}");

            return _regex.IsMatch(value);
        }

        public void Validate()
        {
            if (string.IsNullOrEmpty(Name))
            {
               throw new ValidationException($"HostPropertyCondition {nameof(Name)} cannot be empty.");
            }

            if (!Enum.GetNames(typeof(ConditionHostPropertyName)).Any(x => x.ToLower().Contains(Name)))
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