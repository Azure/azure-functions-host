using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NCli
{
    internal class VerbType
    {
        public Type Type { get; set; }

        public VerbAttribute Metadata { get; set; }

        private IEnumerable<OptionInfo> _options;
        public IEnumerable<OptionInfo> Options
        {
            get
            {
                if (_options == null)
                {
                    _options = Type
                        .GetProperties()
                        .Select(p => new OptionInfo { PropertyInfo = p, Attribute = p.GetCustomAttribute<OptionAttribute>() })
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
