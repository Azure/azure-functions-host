// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Conditions
{
    /// <summary>
    /// Matches the value of an environment variable against a regex pattern.
    /// Invalid configurations (missing name, missing expression, non-compilable regex)
    /// leave the condition in an invalid state so <see cref="Evaluate"/> returns false.
    /// </summary>
    public class EnvironmentCondition : ICondition
    {
        private readonly ILogger _logger;
        private readonly IEnvironment _environment;
        private readonly string _name;
        private readonly string _expression;
        private readonly Regex _regex;
        private readonly bool _isValid;

        public EnvironmentCondition(ILogger logger, IEnvironment environment, ConditionDescriptor descriptor)
        {
            if (descriptor is null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));

            if (descriptor.Properties.TryGetValue(ConditionConstants.ConditionName, out var conditionNameElement))
            {
                _name = conditionNameElement.GetString();
            }

            if (descriptor.Properties.TryGetValue(ConditionConstants.ConditionExpression, out var conditionExpressionElement))
            {
                _expression = conditionExpressionElement.GetString();
            }

            _isValid = TryCompileRegex(_name, _expression, _logger, out _regex);
        }

        public string Name => _name;

        public string Expression => _expression;

        public bool Evaluate()
        {
            if (!_isValid)
            {
                return false;
            }

            string value = _environment.GetEnvironmentVariable(Name);

            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            _logger.LogDebug("Evaluating EnvironmentCondition with name: '{name}', value: '{value}' and expression: '{expression}'", Name, value, Expression);

            return _regex.IsMatch(value);
        }

        private static bool TryCompileRegex(string name, string expression, ILogger logger, out Regex regex)
        {
            regex = null;

            if (string.IsNullOrEmpty(name))
            {
                logger.LogWarning("EnvironmentCondition is missing conditionName; condition will evaluate to false.");
                return false;
            }

            if (string.IsNullOrEmpty(expression))
            {
                logger.LogWarning("EnvironmentCondition for '{name}' is missing conditionExpression; condition will evaluate to false.", name);
                return false;
            }

            try
            {
                regex = new Regex(expression);
                return true;
            }
            catch (ArgumentException)
            {
                logger.LogWarning("EnvironmentCondition for '{name}' has an invalid regex '{expression}'; condition will evaluate to false.", name, expression);
                return false;
            }
        }
    }
}
