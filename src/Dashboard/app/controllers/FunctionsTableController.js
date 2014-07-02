angular.module('dashboard').controller('FunctionsTableController',
    function FunctionsTableController($scope, $rootScope, $http, api, FunctionDefinition) {

        if (!$scope.functionDefinitions || !$scope.functionDefinitions.endpoint) {
            throw Error("Parent scope must define 'functionDefinitions' object, with an 'endpoint' property pointing at the server endpoint for retrieving FunctionDefinition objects.");
        }

        $scope.functionDefinitions.firstPage = true;
        $scope.functionDefinitions.initializing = true;
        $scope.functionDefinitions.indexOfPageIntoDownloaded = 0;
        $scope.functionDefinitions.continuationToken = null;
        $scope.functionDefinitions.reloadFirstPage = function () {
            reloadFirstPage();
        };
        $scope.functionDefinitions.loadNextPage = function () {
            loadNextPage();
        };
        $scope.functionDefinitions.loadPreviousPage = function () {
            loadPreviousPage();
        };

        $scope.$on('functionDefinitions:poll', function () {
            onPoll();
        });

        function reloadFirstPage() {
            var functionDefinitions = $scope.functionDefinitions;

            // Drop all downloaded data.
            functionDefinitions.downloaded = [];
            functionDefinitions.continuationToken = null;
            functionDefinitions.downloadCompleted = false;
            functionDefinitions.pageIndex = 0;

            // Request one more than the page size to know whether to show a next page link.
            // Subsequent requests will get the next page size chunk and still know whether to show a next page link.
            // First request: items 1-11
            // Second request: items 12-21
            // etc.
            downloadSegment({ limit: 11 }, function () {
                functionDefinitions.hasNew = false;
                bindPage();
            })
                ['finally'](function () {
                    functionDefinitions.initializing = false;
                    functionDefinitions.hasNew = false;
                });
        }

        function loadNextPage() {
            var functionDefinitions = $scope.functionDefinitions;
            var downloaded = functionDefinitions.downloaded;

            ++functionDefinitions.pageIndex;

            if (functionDefinitions.downloadCompleted || (downloaded != null
                && downloaded.length > ((functionDefinitions.pageIndex * 10) + 1))) {
                bindPage();
            } else {
                downloadSegment({ limit: 10, continuationToken: functionDefinitions.continuationToken }, function () {
                    bindPage();
                });
            }
        }

        function loadPreviousPage() {
            --$scope.functionDefinitions.pageIndex;
            bindPage();
        }

        function bindPage() {
            var functionDefinitions = $scope.functionDefinitions,
                downloaded = functionDefinitions.downloaded,
                downloadedLength = downloaded.length,
                page,
                pageIndex = functionDefinitions.pageIndex,
                length = Math.min(downloadedLength, (pageIndex + 1) * 10),
                index,
                item;

            functionDefinitions.initializing = false;
            functionDefinitions.page = [];
            page = functionDefinitions.page;
            functionDefinitions.firstPage = pageIndex == 0;

            for (index = pageIndex * 10; index < length; ++index) {
                item = downloaded[index];
                page.push(item);
            }

            functionDefinitions.hasMore = downloadedLength > index;
        }

        function getFunctionDefinitions(params) {
            return $http.get($scope.functionDefinitions.endpoint, { params: params });
        }

        function downloadSegment(params, success) {
            $scope.functionDefinitions.disablePager = true;
            return getFunctionDefinitions(params)
                .success(function () {
                    handleDownloadSegment.apply(this, arguments);
                    if (typeof success === 'function') {
                        success.apply(this, arguments);
                    }
                })
                .error(handleDownloadSegmentError)
                ['finally'](function () {
                    $scope.functionDefinitions.disablePager = false;
                });
        }

        function handleDownloadSegment(data) {
            var entries = data.entries,
                length = entries != null ? entries.length : 0,
                functionDefinitions = $scope.functionDefinitions,
                downloaded = functionDefinitions.downloaded,
                index,
                entry,
                item,
                continuationToken;

            if (data.isOldHost !== undefined) {
                $rootScope.isOldHost = data.isOldHost;
            }

            if (length > 0) {
                for (index = 0; index !== length; ++index) {
                    entry = data.entries[index];
                    item = FunctionDefinition.fromJson(entry);
                    downloaded.push(item);
                }
            }

            continuationToken = data.continuationToken;
            functionDefinitions.continuationToken = continuationToken;

            if (continuationToken == null) {
                functionDefinitions.downloadCompleted = true;
            }
        }

        function handleDownloadSegmentError() {
        }

        function onPoll() {
            var functionDefinitions = $scope.functionDefinitions;

            if (functionDefinitions.initializing && !functionDefinitions.loading) {
                functionDefinitions.loading = true;
                reloadFirstPage();
                return;
            }

            if (functionDefinitions.firstPage && !functionDefinitions.hasNew) {
                updateHasNew();
            }
        }

        function updateHasNew() {
            var functionDefinitions = $scope.functionDefinitions;

            if (!functionDefinitions.page) {
                return;
            }

            if (!functionDefinitions.firstPage || functionDefinitions.hasNew) {
                return;
            }

            var params = {
                limit: 1
            };

            getFunctionDefinitions(params).success(function (data) {
                var entries = data.entries;

                if (entries != null && entries.length > 0 && entries[0].whenUtc > functionDefinitions.page[0].whenUtc) {
                    functionDefinitions.hasNew = true;
                }
            });
        }
   }
);