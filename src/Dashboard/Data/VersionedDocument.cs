using System;

namespace Dashboard.Data
{
    public class VersionedDocument<TDocument>
    {
        private readonly TDocument _document;
        private readonly string _eTag;

        public VersionedDocument(TDocument document, string eTag)
        {
            if (document == null)
            {
                throw new ArgumentNullException("document");
            }
            else if (eTag == null)
            {
                throw new ArgumentNullException("eTag");
            }

            _document = document;
            _eTag = eTag;
        }

        public TDocument Document { get { return _document; } }

        public string ETag { get { return _eTag; } }
    }
}
