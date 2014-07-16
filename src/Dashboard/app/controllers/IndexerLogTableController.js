angular.module('dashboard').controller('IndexerLogTableController',
    function FunctionsTableController($scope, $rootScope, $http, IndexerLogEntry) {

        if (!$scope.indexerLogs || !$scope.indexerLogs.endpoint) {
            throw Error("Parent scope must define 'indexerLogs' object, with an 'endpoint' property pointing at the server endpoint for retrieving IndexerLogEntry objects.");
        }

        $scope.indexerLogs.firstPage = true;
        $scope.indexerLogs.initializing = true;
        $scope.indexerLogs.indexOfPageIntoDownloaded = 0;
        $scope.indexerLogs.continuationToken = null;

        $scope.indexerLogs.loadNextPage = function () {
            loadNextPage();
        };
        $scope.indexerLogs.loadPreviousPage = function () {
            loadPreviousPage();
        };

        function loadFirstPage() {
            var indexerLogs = $scope.indexerLogs;

            // Drop all downloaded data.
            indexerLogs.downloaded = [];
            indexerLogs.continuationToken = null;
            indexerLogs.downloadCompleted = false;
            indexerLogs.pageIndex = 0;

            // Request one more than the page size to know whether to show a next page link.
            // Subsequent requests will get the next page size chunk and still know whether to show a next page link.
            // First request: items 1-11
            // Second request: items 12-21
            // etc.
            downloadSegment({ limit: 11 }, function () {
                indexerLogs.hasNew = false;
                bindPage();
            })

            ['finally'](function () {
                indexerLogs.initializing = false;
                indexerLogs.hasNew = false;
            });
        }

        function loadNextPage() {
            var indexerLogs = $scope.indexerLogs;
            var downloaded = indexerLogs.downloaded;

            ++indexerLogs.pageIndex;

            if (indexerLogs.downloadCompleted || (downloaded != null
                && downloaded.length > ((indexerLogs.pageIndex * 10) + 1))) {
                bindPage();
            } else {
                downloadSegment({ limit: 10, continuationToken: indexerLogs.continuationToken }, function () {
                    bindPage();
                });
            }
        }

        function loadPreviousPage() {
            --$scope.indexerLogs.pageIndex;
            bindPage();
        }

        function bindPage() {
            var indexerLogs = $scope.indexerLogs,
                downloaded = indexerLogs.downloaded,
                downloadedLength = downloaded.length,
                page,
                pageIndex = indexerLogs.pageIndex,
                length = Math.min(downloadedLength, (pageIndex + 1) * 10),
                index,
                item;

            indexerLogs.initializing = false;
            indexerLogs.page = [];
            page = indexerLogs.page;
            indexerLogs.firstPage = pageIndex == 0;

            for (index = pageIndex * 10; index < length; ++index) {
                item = downloaded[index];
                page.push(item);
            }

            indexerLogs.hasMore = downloadedLength > index;
        }

        function getIndexerLogs(params) {
            return $http.get($scope.indexerLogs.endpoint, { params: params });
        }

        function downloadSegment(params, success) {
            $scope.indexerLogs.disablePager = true;
            return getIndexerLogs(params)
                .success(function () {
                    handleDownloadSegment.apply(this, arguments);
                    if (typeof success === 'function') {
                        success.apply(this, arguments);
                    }
                })
                .error(handleDownloadSegmentError)
                ['finally'](function () {
                    $scope.getIndexerLogs.disablePager = false;
                });
        }

        function handleDownloadSegment(data) {
            var entries = data.entries,
                length = entries != null ? entries.length : 0,
                indexerLogs = $scope.indexerLogs,
                downloaded = indexerLogs.downloaded,
                index,
                entry,
                item,
                continuationToken;

            if (length > 0) {
                for (index = 0; index !== length; ++index) {
                    entry = data.entries[index];
                    item = IndexerLogEntry.fromJson(entry);
                    downloaded.push(item);
                }
            }

            continuationToken = data.continuationToken;
            indexerLogs.continuationToken = continuationToken;

            if (continuationToken == null) {
                indexerLogs.downloadCompleted = true;
            }
        }

        function handleDownloadSegmentError() {
        }

        loadFirstPage();
    }
);
