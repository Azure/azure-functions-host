using System;
using System.Threading.Tasks;

namespace NCli
{
    [Verb(ShowInHelp = false)]
    internal class DefaultHelp : IVerb
    {
        private readonly HelpTextCollection _help;

        [Option(0)]
        public string Verb { get; set; }

        public string OriginalVerb { get; set; }

        public IDependencyResolver DependencyResolver { get; set; }

        public DefaultHelp(HelpTextCollection help)
        {
            _help = help;
        }

        public Task RunAsync()
        {
            Console.WriteLine("Azure Functions CLI 0.1");
            _help.ForEach(l => Console.WriteLine(l.ToString()));
            return Task.CompletedTask;
        }
    }
}
