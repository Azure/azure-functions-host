using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorkerHarness.Core.Validators;

namespace WorkerHarness.Core
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
