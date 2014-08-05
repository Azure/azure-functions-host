angular.module('dashboard').controller('InvocationsTableController',
    function InvocationsTableController($scope, $rootScope, $location, $http, api, FunctionInvocationSummary) {

        if (!$scope.invocations || !$scope.invocations.endpoint) {
            throw Error("Parent scope must define 'invocations' object, with an 'endpoint' property pointing at the server endpoint for retrieving Invocation objects.");
        }

        $scope.invocations.firstPage = true;
        $scope.invocations.initializing = true;
        $scope.invocations.downloaded = [];
        $scope.invocations.indexOfPageIntoDownloaded = 0;
        $scope.invocations.continuationToken = null;
        $scope.invocations.nonFinalItemsInPage = {};
        $scope.invocations.reloadFirstPage = function () {
            reloadFirstPage();
        };
        $scope.invocations.loadNextPage = function () {
            loadNextPage();
        };
        $scope.invocations.loadPreviousPage = function () {
            loadPreviousPage();
        };

        $scope.$on('invocations:poll', function () {
            onPoll();
        });

        $scope.$on('invocations:updateTiming', function () {
            onUpdateTiming();
        });


        function reloadFirstPage() {
            var invocations = $scope.invocations;

            // Drop all downloaded data.
            invocations.downloaded = [];
            invocations.continuationToken = null;
            invocations.downloadCompleted = false;
            invocations.pageIndex = 0;

            // Request one more than the page size to know whether to show a next page link.
            // Subsequent requests will get the next page size chunk and still know whether to show a next page link.
            // First request: items 1-11
            // Second request: items 12-21
            // etc.
            downloadSegment({ limit: 11 }, function () {
                invocations.hasNew = false;
                bindPage();
            })
                ['finally'](function () {
                    invocations.initializing = false;
                    invocations.hasNew = false;
                });
        }

        function loadNextPage() {
            var invocations = $scope.invocations;
            var downloaded = invocations.downloaded;

            ++invocations.pageIndex;

            if (invocations.downloadCompleted || (downloaded != null
                && downloaded.length > ((invocations.pageIndex * 10) + 1))) {
                bindPage();
            } else {
                downloadSegment({ limit: 10, continuationToken: invocations.continuationToken }, function () {
                    bindPage();
                });
            }
        }

        function loadPreviousPage() {
            --$scope.invocations.pageIndex;
            bindPage();
        }

        function bindPage() {
            var invocations = $scope.invocations,
                downloaded = invocations.downloaded,
                downloadedLength = downloaded.length,
                nonFinalInvocationsInPage = invocations.nonFinalInvocationsInPage,
                page,
                pageIndex = invocations.pageIndex,
                length = Math.min(downloadedLength, (pageIndex + 1) * 10),
                index,
                item;

            invocations.initializing = false;
            invocations.page = [];
            page = invocations.page;
            invocations.nonFinalInvocationsInPage = {};
            invocations.firstPage = pageIndex == 0;

            for (index = pageIndex * 10; index < length; ++index) {
                item = downloaded[index];

                if (!item.isFinal()) {
                    nonFinalInvocationsInPage[item.id] = item;
                }

                page.push(item);
            }

            invocations.hasMore = downloadedLength > index;
        }

        function getFunctionInvocations(params) {
            return $http.get($scope.invocations.endpoint, { params: params });
        }

        function downloadSegment(params, success) {
            $scope.invocations.disablePager = true;
            return getFunctionInvocations(params)
                .success(function () {
                    handleDownloadSegment.apply(this, arguments);
                    if (typeof success === 'function') {
                        success.apply(this, arguments);
                    }
                })
                .error(handleDownloadSegmentError)
                ['finally'](function () {
                    $scope.invocations.disablePager = false;
                });
        }

        function handleDownloadSegment(data) {
            var entries = data.entries,
                length = entries != null ? entries.length : 0,
                invocations = $scope.invocations,
                downloaded = invocations.downloaded,
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
                    item = FunctionInvocationSummary.fromJson(entry);
                    downloaded.push(item);
                }
            }

            continuationToken = data.continuationToken;
            invocations.continuationToken = continuationToken;

            if (continuationToken == null) {
                invocations.downloadCompleted = true;
            }
        }

        function handleDownloadSegmentError() {
        }

        function onPoll() {
            var invocations = $scope.invocations;

            if (invocations.initializing && !invocations.loading) {
                invocations.loading = true;
                reloadFirstPage();
                return;
            }

            if (invocations.firstPage && !invocations.hasNew) {
                updateHasNew();
            }

            updateNonFinalItemsInPage();
        }

        function onUpdateTiming() {
            var invocations = $scope.invocations;

            if (!invocations.page) {
                return;
            }

            var page = invocations.page,
                length = page.length,
                index;

            for (index = 0; index !== length; ++index) {
                page[index].updateTimingStrings();
            }
        }

        function updateHasNew() {
            var invocations = $scope.invocations;

            if (!invocations.page) {
                return;
            }

            if (!invocations.firstPage || invocations.hasNew) {
                return;
            }

            if (invocations.skipHasNewPolling) {
                return;
            }

            var params = {
                limit: 1
            };

            getFunctionInvocations(params).success(function (data) {
                var entries = data.entries;

                if (entries != null && entries.length > 0 && entries[0].whenUtc > invocations.page[0].whenUtc) {
                    invocations.hasNew = true;
                }
            }).error(function (res, code, data) {
                $rootScope.errors.push('Error while getting function invocations (Error code: ' + code + ')');
                $location.url('/');
            });
        }

        function updateNonFinalItemsInPage() {
            var nonFinalIds = Object.getOwnPropertyNames($scope.invocations.nonFinalItemsInPage);
            if (nonFinalIds.length === 0) {
                return;
            }
            $http.post(api.sdk.invocationByIds(), JSON.stringify(nonFinalIds)).success(function (data) {
                var invocations = $scope.invocations,
                    nonFinalItemsInPage = invocations.nonFinalItemsInPage,
                    length = data.length,
                    index,
                    invocation,
                    item;

                for (index = 0; index !== length; ++index) {
                    item = data[index];
                    invocation = nonFinalItemsInPage[item.id];
                    if (invocation) {
                        invocation.functionDisplayTitle = item.functionDisplayTitle;
                        invocation.status = item.status;
                        invocation.when = item.whenUtc;
                        invocation.duration = item.duration;
                        invocation.exceptionMessage = item.exceptionMessage;
                        invocation.updateTimingStrings();
                        if (invocation.isFinal()) {
                            delete nonFinalItemsInPage[invocation.id];
                        }
                    }
                }
            }).error(function (res, code) {
                $rootScope.errors.push('Error while updating data (Error code: ' + code + ')');
            });
        }
    }
);
