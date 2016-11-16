// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Host.Bindings.Path
{
    internal abstract class BindingParameterResolver
    {
        private static Collection<BindingParameterResolver> _resolvers;

        static BindingParameterResolver()
        {
            // create the static set of built in system resolvers
            _resolvers = new Collection<BindingParameterResolver>();
            _resolvers.Add(new RandGuidResolver());
            _resolvers.Add(new DateTimeResolver());
        }

        public abstract string Name { get; }

        public static bool IsSystemParameter(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentNullException("value");
            }

            BindingParameterResolver resolver = null;
            return TryGetResolver(value, out resolver);
        }

        public static bool TryGetResolver(string value, out BindingParameterResolver resolver)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentNullException("value");
            }

            resolver = _resolvers.FirstOrDefault(p => value.StartsWith(p.Name, StringComparison.OrdinalIgnoreCase));
            return resolver != null;
        }

        public abstract string Resolve(string value);

        protected string GetFormatOrNull(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentNullException("value");
            }

            if (!value.StartsWith(Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "The value specified is not a '{0}' binding parameter.", Name), "value");
            }

            if (value.Length > Name.Length && value[Name.Length] == ':')
            {
                // we have a value of format <Name>:<Format>
                // parse out everything after the first colon
                int idx = Name.Length;
                return value.Substring(idx + 1);
            }

            return null;
        }

        private class RandGuidResolver : BindingParameterResolver
        {
            public override string Name
            {
                get
                {
                    return "rand-guid";
                }
            }

            public override string Resolve(string value)
            {
                string format = GetFormatOrNull(value);

                if (!string.IsNullOrEmpty(format))
                {
                    return Guid.NewGuid().ToString(format, CultureInfo.InvariantCulture);
                }
                else
                {
                    return Guid.NewGuid().ToString();
                }
            }
        }

        private class DateTimeResolver : BindingParameterResolver
        {
            public override string Name
            {
                get
                {
                    return "datetime";
                }
            }

            public override string Resolve(string value)
            {
                string format = GetFormatOrNull(value);

                if (!string.IsNullOrEmpty(format))
                {
                    return DateTime.Now.ToString(format, CultureInfo.InvariantCulture);
                }
                else
                {
                    // default to ISO 8601
                    return DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ssK", CultureInfo.InvariantCulture);
                }
            }
        }
    }
}
