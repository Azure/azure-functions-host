angular.module('dashboard').controller('IndexerLogEntryController',
    function FunctionsTableController($scope, $routeParams, $rootScope, $http, api, IndexerLogEntry) {
        $scope.entry = {
            endpoint: api.sdk.indexerLogEntry($routeParams.entryId)
        };

        $scope.entry.initializing = true;
        $scope.entry.notFound = false;


        function downloadIndexerLogEntry() {
            return $http.get($scope.entry.endpoint);
        }

        function getLogEntry() {
            return downloadIndexerLogEntry()
                .success(function () {
                    handleDownloadSuccess.apply(this, arguments);
                    if (typeof success === 'function') {
                        success.apply(this, arguments);
                    }
                })
                .error(handleDownloadError)
                ['finally'](function () {
                    $scope.entry.initializing = false;
                });
        }

        function handleDownloadSuccess(data) {
            var entry = $scope.entry;
            entry.item = IndexerLogEntry.fromJson(data);
        }

        function handleDownloadError() {
            var entry = $scope.entry;
            entry.notFound = true;
        }

        getLogEntry();
    }
);