using Newtonsoft.Json;

namespace Dashboard.Data
{
    public class JsonVersionedDocumentStore<TDocument> : IVersionedDocumentStore<TDocument>
    {
        private static readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented
        };

        private readonly IVersionedTextStore _innerStore;

        public JsonVersionedDocumentStore(IVersionedTextStore innerStore)
        {
            _innerStore = innerStore;
        }

        internal static JsonSerializerSettings JsonSerializerSettings
        {
            get { return _settings; }
        }

        public VersionedDocument<TDocument> Read(string id)
        {
            VersionedText innerResult = _innerStore.Read(id);

            if (innerResult == null)
            {
                return null;
            }


            TDocument document = JsonConvert.DeserializeObject<TDocument>(innerResult.Text, _settings);
            string eTag = innerResult.ETag;
            return new VersionedDocument<TDocument>(document, eTag);
        }

        public void CreateOrUpdate(string id, TDocument document)
        {
            string text = JsonConvert.SerializeObject(document, _settings);

            _innerStore.CreateOrUpdate(id, text);
        }

        public bool TryCreate(string id, TDocument document)
        {
            string text = JsonConvert.SerializeObject(document, _settings);

            return _innerStore.TryCreate(id, text);
        }

        public bool TryUpdate(string id, TDocument document, string eTag)
        {
            string text = JsonConvert.SerializeObject(document, _settings);

            return _innerStore.TryUpdate(id, text, eTag);
        }

        public bool TryDelete(string id, string eTag)
        {
            return _innerStore.TryDelete(id, eTag);
        }
    }
}
