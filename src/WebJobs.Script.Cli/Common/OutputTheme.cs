using Colors.Net;
using static Colors.Net.StringStaticMethods;

namespace WebJobs.Script.Cli.Common
{
    public static class OutputTheme
    {
        public static RichString TitleColor(string value) => DarkCyan(value);
        public static RichString VerboseColor(string value) => Green(value);
        public static RichString AdditionalInfoColor(string value) => Cyan(value);
        public static RichString ExampleColor(string value) => DarkYellow(value);
        public static RichString ErrorColor(string value) => Red(value);
        public static RichString QuestionColor(string value) => DarkMagenta(value);

    }
}