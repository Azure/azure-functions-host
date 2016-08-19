using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Permissions;
using System.Text;
using System.Threading.Tasks;

namespace WebJobs.Script.Cli.Common
{
    [Serializable]
    public class CliException : Exception
    {
        public bool Handled { get; set; }

        public CliException(string message) : base(message)
        { }

        public CliException(string message, Exception innerException) : base(message, innerException)
        { }

        public CliException()
        { }

        protected CliException(SerializationInfo info, StreamingContext context) : base(info, context)
        { }

        [SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            info.AddValue("Handled", Handled);
            base.GetObjectData(info, context);
        }
    }
}
