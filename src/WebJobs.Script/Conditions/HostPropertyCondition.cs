// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Conditions
{
    /// <summary>
    /// Matches a host property (Sku, Platform, HostVersion) against a regex pattern.
    /// Invalid configurations (unknown property name, missing expression, non-compilable
    /// regex) leave the condition in an invalid state so <see cref="Evaluate"/> returns false.
    /// </summary>
    public class HostPropertyCondition : ICondition
    {
        private readonly ILogger _logger;
        private readonly ISystemRuntimeInformation _systemRuntimeInformation;
        private readonly string _name;
        private readonly string _expression;
        private readonly Regex _regex;
        private readonly HostProperty? _property;
        private readonly bool _isValid;

        public HostPropertyCondition(ILogger logger, ISystemRuntimeInformation systemRuntimeInformation, ConditionDescriptor descriptor)
        {
            if (descriptor is null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemRuntimeInformation = systemRuntimeInformation ?? throw new ArgumentNullException(nameof(systemRuntimeInformation));

            if (descriptor.Properties.TryGetValue(ConditionConstants.ConditionName, out var conditionNameElement))
            {
                _name = conditionNameElement.GetString();
            }

            if (descriptor.Properties.TryGetValue(ConditionConstants.ConditionExpression, out var conditionExpressionElement))
            {
                _expression = conditionExpressionElement.GetString();
            }

            _isValid = TryResolveProperty(_name, _logger, out _property)
                       && TryCompileRegex(_name, _expression, _logger, out _regex);
        }

        private enum HostProperty
        {
            Sku,
            Platform,
            HostVersion
        }

        public string Name => _name;

        public string Expression => _expression;

        public bool Evaluate()
        {
            if (!_isValid)
            {
                return false;
            }

            string value = _property switch
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

            _logger.LogDebug("Evaluating HostPropertyCondition with name: {name}, value: {value} and expression {expression}", Name, value, Expression);

            return _regex.IsMatch(value);
        }

        private static bool TryResolveProperty(string name, ILogger logger, out HostProperty? property)
        {
            property = null;

            if (string.IsNullOrEmpty(name))
            {
                logger.LogWarning("HostPropertyCondition is missing conditionName; condition will evaluate to false.");
                return false;
            }

            if (Enum.TryParse(typeof(HostProperty), name, ignoreCase: true, out object parsed))
            {
                property = (HostProperty)parsed;
                return true;
            }

            logger.LogWarning("HostPropertyCondition conditionName '{name}' is not a known host property; condition will evaluate to false.", name);
            return false;
        }

        private static bool TryCompileRegex(string name, string expression, ILogger logger, out Regex regex)
        {
            regex = null;

            if (string.IsNullOrEmpty(expression))
            {
                logger.LogWarning("HostPropertyCondition for '{name}' is missing conditionExpression; condition will evaluate to false.", name);
                return false;
            }

            try
            {
                regex = new Regex(expression);
                return true;
            }
            catch (ArgumentException)
            {
                logger.LogWarning("HostPropertyCondition for '{name}' has an invalid regex '{expression}'; condition will evaluate to false.", name, expression);
                return false;
            }
        }
    }
}
