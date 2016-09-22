using System;
using System.Text;
using static NCli.Constants;

namespace NCli
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1019:DefineAccessorsForAttributeArguments")]
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class OptionAttribute : Attribute
    {
        public object DefaultValue { get; set; }

        public string HelpText { get; set; }

        public bool ShowInHelp { get; set; } = true;

        internal char _shortName { get; }

        internal string _longName { get; }

        internal int _order { get; }

        public OptionAttribute(char shortName, string longName)
        {
            _shortName = shortName;
            _longName = longName;
            _order = -1;
        }

        public OptionAttribute(string longName) : this(NullCharacter, longName)
        { }

        public OptionAttribute(char shortName) : this(shortName, string.Empty)
        { }

        public OptionAttribute(int order) : this(NullCharacter, string.Empty)
        {
            _order = order;
        }

        internal string GetUsage(string propertyName)
        {
            if (_order != -1)
            {
                return $"<{char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1)}>";
            }

            var builder = new StringBuilder();

            if (_shortName != NullCharacter)
            {
                builder.Append($"-{_shortName}");
            }

            if (!string.IsNullOrEmpty(_longName))
            {
                if (builder.Length != 0)
                {
                    builder.Append("/");
                }
                builder.Append($"--{_longName}");
            }

            return builder.ToString();
        }
    }
}
