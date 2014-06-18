using System;

namespace Dashboard.Data
{
    public class VersionedText
    {
        private readonly string _text;
        private readonly string _eTag;

        public VersionedText(string text, string eTag)
        {
            if (text == null)
            {
                throw new ArgumentNullException("text");
            }
            else if (eTag == null)
            {
                throw new ArgumentNullException("eTag");
            }

            _text = text;
            _eTag = eTag;
        }

        public string Text { get { return _text; } }

        public string ETag { get { return _eTag; } }
    }
}
