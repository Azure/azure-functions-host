// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Dashboard.Data
{
    public interface IConcurrentDocumentStore<TDocument>
    {
        IConcurrentDocument<TDocument> Read(string id);

        void CreateOrUpdate(string id, TDocument document);

        bool TryCreate(string id, TDocument document);

        bool TryUpdate(string id, string eTag, TDocument document);

        bool TryDelete(string id, string eTag);
    }
}
