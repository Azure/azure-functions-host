using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NCli
{
    internal class TypePair<T> where T: Attribute
    {
        public Type Type { get; set; }

        public T Attribute { get; set; }

        private IEnumerable<PropertyInfoPair<OptionAttribute>> _options;
        public IEnumerable<PropertyInfoPair<OptionAttribute>> Options
        {
            get
            {
                if (_options == null)
                {
                    _options = Type
                        .GetProperties()
                        .Select(p => new PropertyInfoPair<OptionAttribute> { PropertyInfo = p, Attribute = p.GetCustomAttribute<OptionAttribute>() })
                        .Where(a => a.Attribute != null);
                }

                return _options;
            }
            set
            {
                this._options = value;
            }
        }

    }
}
