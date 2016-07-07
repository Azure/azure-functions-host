using System;
using System.Runtime.Serialization;

namespace NCli
{
    [Serializable]
    public class ParseException : Exception
    {
        public ParseException(string message) : base(message)
        { }

        public ParseException(string message, Exception innerException) : base(message, innerException)
        { }

        public ParseException()
        { }

        protected ParseException(SerializationInfo info, StreamingContext context) : base(info, context)
        { }
    }
}
