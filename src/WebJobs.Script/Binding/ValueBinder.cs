// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;

namespace Microsoft.Azure.WebJobs.Extensions.Bindings
{
    /// <summary>
    /// Base class for value binders.
    /// </summary>
    internal abstract class ValueBinder : IOrderedValueBinder
    {
        private readonly Type _type;
        private readonly BindStepOrder _bindStepOrder;

        /// <summary>
        /// Initializes a new instance of the <see cref="ValueBinder"/> class.
        /// </summary>
        /// <param name="type">The <see cref="Type"/> of the value.</param>
        /// <param name="bindStepOrder">The <see cref="BindStepOrder"/>.</param>
        protected ValueBinder(Type type, BindStepOrder bindStepOrder = BindStepOrder.Default)
        {
            _type = type;
            _bindStepOrder = bindStepOrder;
        }

        /// <inheritdoc/>
        public BindStepOrder StepOrder
        {
            get { return _bindStepOrder; }
        }

        /// <inheritdoc/>
        public Type Type
        {
            get { return _type; }
        }

        /// <inheritdoc/>
        public abstract Task<object> GetValueAsync();

        /// <inheritdoc/>
        public abstract string ToInvokeString();

        /// <inheritdoc/>
        public virtual Task SetValueAsync(object value, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        /// <summary>
        /// Determines whether the Type of the specified parameter matches one of the specified
        /// types.
        /// </summary>
        /// <param name="parameter">The parameter to check.</param>
        /// <param name="types">The set of types to check against.</param>
        /// <returns>True if a match is found, false otherwise.</returns>
        public static bool MatchParameterType(ParameterInfo parameter, IEnumerable<Type> types)
        {
            if (parameter == null)
            {
                throw new ArgumentNullException(nameof(parameter));
            }
            if (types == null)
            {
                throw new ArgumentNullException(nameof(types));
            }

            if (parameter.IsOut)
            {
                return types.Any(p => p.MakeByRefType() == parameter.ParameterType);
            }
            else
            {
                return types.Contains(parameter.ParameterType);
            }
        }
    }
}
