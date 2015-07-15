// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Blobs.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Blobs.Listeners
{
    public class StorageBlobContainerExtensionsTests
    {
        [Theory]
        [InlineData(4000)]
        [InlineData(30000)]
        public async Task ListBlobsAsync_FollowsContinuationTokensToEnd(int blobCount)
        {
            Mock<IStorageBlobContainer> mockContainer = new Mock<IStorageBlobContainer>(MockBehavior.Strict);

            int maxResults = 5000;
            List<IStorageListBlobItem> blobs = GetMockBlobs(blobCount);
            int numPages = (int)Math.Ceiling(((decimal)blobCount / maxResults));

            // create the test data pages with continuation tokens
            List<IStorageBlobResultSegment> blobSegments = new List<IStorageBlobResultSegment>();
            IStorageBlobResultSegment initialSegement = null;
            for (int i = 0; i < numPages; i++)
            {
                BlobContinuationToken continuationToken = null;
                if (i < (numPages - 1))
                {
                    // add a token for all but the last page
                    continuationToken = new BlobContinuationToken()
                    {
                        NextMarker = i.ToString()
                    };
                }

                Mock<IStorageBlobResultSegment> mockSegment = new Mock<IStorageBlobResultSegment>(MockBehavior.Strict);
                mockSegment.SetupGet(p => p.Results).Returns(blobs.Skip(i * maxResults).Take(maxResults).ToArray());
                mockSegment.SetupGet(p => p.ContinuationToken).Returns(continuationToken);

                if (i == 0)
                {
                    initialSegement = mockSegment.Object;
                }
                else
                {
                    blobSegments.Add(mockSegment.Object);
                }
            }

            // Mock the List function to return the correct blob page
            HashSet<BlobContinuationToken> seenTokens = new HashSet<BlobContinuationToken>();
            IStorageBlobResultSegment resultSegment = null;
            mockContainer.Setup(p => p.ListBlobsSegmentedAsync(null, true, BlobListingDetails.None, null, It.IsAny<BlobContinuationToken>(), null, null, CancellationToken.None))
                .Callback<string, bool, BlobListingDetails, int?, BlobContinuationToken, BlobRequestOptions, OperationContext, CancellationToken>(
                    (prefix, useFlatBlobListing, blobListingDetails, maxResultsValue, currentToken, options, operationContext, cancellationToken) =>
                    {
                        // Previously this is where a bug existed - ListBlobsAsync wasn't handling
                        // continuation tokens properly and kept sending the same initial token
                        Assert.False(seenTokens.Contains(currentToken));
                        seenTokens.Add(currentToken);

                        if (currentToken == null)
                        {
                            resultSegment = initialSegement;
                        }
                        else
                        {
                            int idx = int.Parse(currentToken.NextMarker);
                            resultSegment = blobSegments[idx];
                        }
                    })
                .Returns(() => { return Task.FromResult(resultSegment); });

            IEnumerable<IStorageListBlobItem> results = await mockContainer.Object.ListBlobsAsync(true, CancellationToken.None);

            Assert.Equal(blobCount, results.Count());
        }

        private class FakeBlobItem : IStorageListBlobItem
        {
        }

        private List<IStorageListBlobItem> GetMockBlobs(int count)
        {
            List<IStorageListBlobItem> blobs = new List<IStorageListBlobItem>();
            for (int i = 0; i < count; i++)
            {
                blobs.Add(new FakeBlobItem());
            }
            return blobs;
        }
    }
}
