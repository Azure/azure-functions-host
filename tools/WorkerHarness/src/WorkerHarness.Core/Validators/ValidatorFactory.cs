// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core.Validators
{
    /// <summary>
    /// A default implementation of IValidatorFactory interface
    /// </summary>
    public class ValidatorFactory : IValidatorFactory
    {
        /// <summary>
        /// Currently support 2 types of validators:
        ///     - string validator: support string comparison
        ///     - regex validator: support regex matching
        /// </summary>
        /// <param name="validatorType"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public IValidator Create(string validatorType)
        {
            IValidator validator;

            if (validatorType.Equals("regex"))
            {
                validator = new RegexValidator();
            }
            else if (validatorType.Equals("string"))
            {
                validator = new StringValidator();
            }
            else
            {
                throw new ArgumentException($"The {nameof(validatorType)} validator does not exist");
            }

            return validator;
        }
    }
}
