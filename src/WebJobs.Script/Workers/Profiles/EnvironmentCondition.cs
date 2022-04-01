// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public class EnvironmentCondition : IWorkerProfileCondition
    {
        private readonly ILogger _logger;
        private readonly IEnvironment _environment;

        public EnvironmentCondition(ILogger logger, IEnvironment environment, string name, string expression)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            Name = name;
            Expression = expression;
            Validate();
        }

        public string Name { get; set; }

        public string Expression { get; set; }

        public bool Evaluate()
        {
            string value = _environment.GetEnvironmentVariable(Name);
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            _logger.LogDebug($"Evaluating EnvironmentCondition with value: {value} and expression {Expression}");
            return Regex.IsMatch(value, Expression);
        }

        public void Validate()
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
                new Regex(Expression);
            }
            catch
            {
                throw new ValidationException($"EnvironmentCondition {nameof(Expression)} must be a valid regular expression.");
            }
        }
    }
}