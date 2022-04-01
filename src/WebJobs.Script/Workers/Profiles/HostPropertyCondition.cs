// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public class HostPropertyCondition : IWorkerProfileCondition
    {
        private readonly ILogger _logger;
        private readonly ISystemRuntimeInformation _systemRuntimeInformation;

        public HostPropertyCondition(ILogger logger, ISystemRuntimeInformation systemRuntimeInformation, string name, string expression)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemRuntimeInformation = systemRuntimeInformation ?? throw new ArgumentNullException(nameof(systemRuntimeInformation));
            Name = name;
            Expression = expression;
            Validate();
        }

        public enum ConditionHostPropertyName
        {
            Sku,
            Platform,
            HostVersion
        }

        public string Name { get; set; }

        public string Expression { get; set; }

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
            return Regex.IsMatch(value, Expression);
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
               new Regex(Expression);
            }
            catch
            {
              throw new ValidationException($"HostPropertyCondition {nameof(Expression)} must be a valid regular expression.");
            }
        }
    }
}