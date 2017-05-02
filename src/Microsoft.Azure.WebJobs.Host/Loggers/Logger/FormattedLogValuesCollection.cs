// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Internal;

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// The <see cref="FormattedLogValues"/> object created by the framework <see cref="ILogger"/> extensions
    /// (LogInformation(), for example) require that all key-value pairs be included in the string message
    /// passed to the method. We'd like to have short strings with a subset of the data, so this class wraps
    /// <see cref="FormattedLogValuesCollection"/> and allows us to use the same behavior, but with an additional
    /// payload not included in the message.
    /// </summary>
    internal class FormattedLogValuesCollection : IReadOnlyList<KeyValuePair<string, object>>
    {
        private FormattedLogValues _formatter;
        private IReadOnlyList<KeyValuePair<string, object>> _additionalValues;

        public FormattedLogValuesCollection(string format, object[] formatValues, IReadOnlyDictionary<string, object> additionalValues)
        {
            if (formatValues != null)
            {
                _formatter = new FormattedLogValues(format, formatValues);
            }
            else
            {
                _formatter = new FormattedLogValues(format);
            }

            _additionalValues = additionalValues?.ToList();

            if (_additionalValues == null)
            {
                _additionalValues = new List<KeyValuePair<string, object>>();
            }
        }

        public int Count => _formatter.Count + _additionalValues.Count;

        public KeyValuePair<string, object> this[int index]
        {
            get
            {
                if (index < 0 || index >= Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                if (index < _additionalValues.Count)
                {
                    // if the index is lower, return the value from _additionalValues
                    return _additionalValues[index];
                }
                else
                {
                    // if there are no more additionalValues, return from _formatter
                    return _formatter[index - _additionalValues.Count];
                }
            }
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            for (int i = 0; i < Count; ++i)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => _formatter.ToString();
    }
}
