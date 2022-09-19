// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.Profiles
{
    /// <summary>
    /// An implementation of an <see cref="IWorkerProfileCondition"/> that checks if
    /// environment variables match the expected output
    /// </summary>
    public class EnvironmentCondition : IWorkerProfileCondition
    {
        private readonly ILogger _logger;
        private readonly IEnvironment _environment;
        private readonly string _name;
        private readonly string _expression;
        private Regex _regex;

        internal EnvironmentCondition(ILogger logger, IEnvironment environment, WorkerProfileConditionDescriptor descriptor)
        {
            if (descriptor is null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));

            descriptor.Properties.TryGetValue(WorkerConstants.WorkerDescriptionProfileConditionName, out _name);
            descriptor.Properties.TryGetValue(WorkerConstants.WorkerDescriptionProfileConditionExpression, out _expression);

            Validate();
        }

        public string Name => _name;

        public string Expression => _expression;

        /// <inheritdoc />
        public bool Evaluate()
        {
            string value = _environment.GetEnvironmentVariable(Name);

            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            _logger.LogDebug("Evaluating EnvironmentCondition with name: '{name}', value: '{value}' and expression: '{expression}'", Name, value, Expression);

            return _regex.IsMatch(value);
        }

        // Validates if condition parameters meet expected values, fail if they don't
        private void Validate()
        {
            if (string.IsNullOrEmpty(Name))
            {
                throw new ValidationException($"EnvironmentCondition {nameof(Name)} cannot be empty.");
            }

            if (string.IsNullOrEmpty(Expression))
            {
                throw new ValidationException($"EnvironmentCondition {nameof(Expression)} cannot be empty.");
            }

            try
            {
                _regex = new Regex(Expression);
            }
            catch
            {
                throw new ValidationException($"EnvironmentCondition {nameof(Expression)} must be a valid regular expression.");
            }
        }
    }
}