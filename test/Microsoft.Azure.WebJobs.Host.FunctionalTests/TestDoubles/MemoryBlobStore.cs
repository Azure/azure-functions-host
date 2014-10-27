// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Shared.Protocol;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class MemoryBlobStore
    {
        private readonly ConcurrentDictionary<string, Container> _items = new ConcurrentDictionary<string, Container>();
        private ServiceProperties _properties = new ServiceProperties();

        public string AcquireLease(string containerName, string blobName, TimeSpan? leaseTime)
        {
            return _items[containerName].AcquireLease(blobName, leaseTime);
        }

        public void CreateIfNotExists(string containerName)
        {
            _items.AddOrUpdate(containerName, new Container(), (_, existing) => existing);
        }

        public bool Exists(string containerName)
        {
            return _items.ContainsKey(containerName);
        }

        public bool Exists(string containerName, string blobName)
        {
            return _items[containerName].Exists(blobName);
        }

        public BlobAttributes FetchAttributes(string containerName, string blobName)
        {
            if (!_items.ContainsKey(containerName))
            {
                throw StorageExceptionFactory.Create(404);
            }

            return _items[containerName].FetchAttributes(blobName);
        }

        public ServiceProperties GetServiceProperties()
        {
            return Clone(_properties);
        }

        public IStorageBlobResultSegment ListBlobsSegmented(string prefix, bool useFlatBlobListing,
            BlobListingDetails blobListingDetails, int? maxResults, BlobContinuationToken currentToken)
        {
            if (prefix == null)
            {
                throw new ArgumentNullException("prefix");
            }

            if (!useFlatBlobListing)
            {
                throw new NotImplementedException();
            }

            if (maxResults.HasValue)
            {
                throw new NotImplementedException();
            }

            if (blobListingDetails != BlobListingDetails.None && blobListingDetails != BlobListingDetails.Metadata)
            {
                throw new NotImplementedException();
            }

            if (prefix.StartsWith("$logs/"))
            {
                return null;
            }

            if (prefix.Contains("/"))
            {
                throw new NotImplementedException();
            }

            if (!_items.ContainsKey(prefix))
            {
                return null;
            }

            IStorageBlobClient client = new FakeStorageBlobClient(this, FakeStorageAccount.DefaultCredentials);
            IStorageBlobContainer parent = new FakeStorageBlobContainer(this, prefix, client);
            IEnumerable<IStorageBlob> results = _items[prefix].ListBlobs(this, parent, blobListingDetails);
            return new StorageBlobResultSegment(null, results);
        }

        public Stream OpenRead(string containerName, string blobName)
        {
            return _items[containerName].OpenRead(blobName);
        }

        public CloudBlobStream OpenWrite(string containerName, string blobName, IDictionary<string, string> metadata)
        {
            if (!_items.ContainsKey(containerName))
            {
                throw StorageExceptionFactory.Create(404, "ContainerNotFound");
            }

            return _items[containerName].OpenWrite(blobName, metadata);
        }

        public void ReleaseLease(string containerName, string blobName, string leaseId)
        {
            _items[containerName].ReleaseLease(blobName, leaseId);
        }

        public void SetServiceProperties(ServiceProperties properties)
        {
            _properties = Clone(properties);
        }

        private static ServiceProperties Clone(ServiceProperties properties)
        {
            if (properties == null)
            {
                return null;
            }

            return new ServiceProperties
            {
                Cors = Clone(properties.Cors),
                DefaultServiceVersion = properties.DefaultServiceVersion,
                HourMetrics = Clone(properties.HourMetrics),
                Logging = Clone(properties.Logging),
#pragma warning disable 0618
                Metrics = Clone(properties.Metrics),
#pragma warning restore 0618
                MinuteMetrics = Clone(properties.MinuteMetrics)
            };
        }

        private static CorsProperties Clone(CorsProperties cors)
        {
            if (cors == null)
            {
                return null;
            }

            CorsProperties cloned = new CorsProperties();

            foreach (CorsRule rule in cors.CorsRules)
            {
                cloned.CorsRules.Add(Clone(rule));
            }

            return cloned;
        }

        private static CorsRule Clone(CorsRule rule)
        {
            throw new NotImplementedException();
        }

        private static MetricsProperties Clone(MetricsProperties metrics)
        {
            if (metrics == null)
            {
                return null;
            }

            return new MetricsProperties
            {
                MetricsLevel = metrics.MetricsLevel,
                RetentionDays = metrics.RetentionDays,
                Version = metrics.Version
            };
        }

        private static LoggingProperties Clone(LoggingProperties logging)
        {
            if (logging == null)
            {
                return null;
            }

            return new LoggingProperties
            {
                LoggingOperations = logging.LoggingOperations,
                RetentionDays = logging.RetentionDays,
                Version = logging.Version
            };
        }

        private class Container
        {
            private readonly ConcurrentDictionary<string, Blob> _items = new ConcurrentDictionary<string, Blob>();

            public string AcquireLease(string blobName, TimeSpan? leaseTime)
            {
                return _items[blobName].AcquireLease(leaseTime);
            }

            public bool Exists(string blobName)
            {
                return _items.ContainsKey(blobName);
            }

            public BlobAttributes FetchAttributes(string blobName)
            {
                return _items[blobName].FetchAttributes();
            }

            public IEnumerable<IStorageBlob> ListBlobs(MemoryBlobStore store, IStorageBlobContainer parent,
                BlobListingDetails blobListingDetails)
            {
                if (blobListingDetails != BlobListingDetails.None && blobListingDetails != BlobListingDetails.Metadata)
                {
                    throw new NotImplementedException();
                }
                List<IStorageBlob> results = new List<IStorageBlob>();

                foreach (KeyValuePair<string, Blob> item in _items)
                {
                    string blobName = item.Key;
                    IStorageBlob blob = new FakeStorageBlockBlob(store, blobName, parent);

                    if ((blobListingDetails | BlobListingDetails.Metadata) == BlobListingDetails.Metadata)
                    {
                        Blob storeBlob = item.Value;
                        IReadOnlyDictionary<string, string> storeMetadata = storeBlob.Metadata;

                        foreach (KeyValuePair<string, string> pair in storeMetadata)
                        {
                            blob.Metadata.Add(pair.Key, pair.Value);
                        }
                    }

                    results.Add(blob);
                }

                return results;
            }

            public Stream OpenRead(string blobName)
            {
                return new MemoryStream(_items[blobName].Contents, writable: false);
            }

            public CloudBlobStream OpenWrite(string blobName, IDictionary<string, string> metadata)
            {
                if (_items.ContainsKey(blobName))
                {
                    _items[blobName].ThrowIfLeased();
                }

                string eTag = Guid.NewGuid().ToString();
                return new MemoryCloudBlobStream((bytes) => _items[blobName] = new Blob(bytes, eTag, DateTimeOffset.Now,
                    metadata));
            }

            public void ReleaseLease(string blobName, string leaseId)
            {
                _items[blobName].ReleaseLease(leaseId);
            }
        }

        private class Blob
        {
            private readonly byte[] _contents;
            private readonly string _eTag;
            private readonly DateTimeOffset _lastModified;
            private readonly IReadOnlyDictionary<string, string> _metadata;

            public Blob(byte[] contents, string eTag, DateTimeOffset lastModified, IDictionary<string, string> metadata)
            {
                _contents = new byte[contents.LongLength];
                _eTag = eTag;
                _lastModified = lastModified;
                Array.Copy(contents, _contents, _contents.LongLength);
                _metadata = new Dictionary<string, string>(metadata);
            }

            public byte[] Contents
            {
                get { return _contents; }
            }

            public string ETag
            {
                get { return _eTag; }
            }

            public DateTimeOffset LastModified
            {
                get { return _lastModified; }
            }

            public string LeaseId { get; private set; }

            public DateTime? LeaseExpires { get; private set; }

            public IReadOnlyDictionary<string, string> Metadata
            {
                get { return _metadata; }
            }

            public string AcquireLease(TimeSpan? leaseTime)
            {
                ThrowIfLeased();

                string leaseId = Guid.NewGuid().ToString();
                LeaseId = leaseId;

                if (leaseTime.HasValue)
                {
                    LeaseExpires = DateTime.UtcNow.Add(leaseTime.Value);
                }
                else
                {
                    LeaseExpires = null;
                }

                return leaseId;
            }

            public BlobAttributes FetchAttributes()
            {
                return new BlobAttributes(_eTag, _lastModified, _metadata);
            }

            public void ReleaseLease(string leaseId)
            {
                if (LeaseId != leaseId)
                {
                    throw new InvalidOperationException();
                }

                LeaseId = null;
                LeaseExpires = null;
            }

            public void ThrowIfLeased()
            {
                if (LeaseId != null)
                {
                    if (!LeaseExpires.HasValue || LeaseExpires.Value > DateTime.UtcNow)
                    {
                        throw new InvalidOperationException();
                    }
                }
            }
        }
    }
}
