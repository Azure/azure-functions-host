angular.module('dashboard').controller('FunctionsHomeController',
    function ($scope, $routeParams, $interval, $http, stringUtils, FunctionDefinition, api, urls) {
        var poll,
            pollInterval = 10 * 1000,
            lastPoll = 0;

        $scope.breadcrumbs = [];

        function getFunctionDefinitions() {
            return $http.get(api.sdk.functionDefinitions()).then(function (res) {
                var ix,
                    item,
                    functionDefinition,
                    result = res.data.functionStatisticsViewModels,
                    len = result.length;
                $scope.functionDefinitions = [];
                for (ix = 0; ix !== len; ++ix) {
                    item = result[ix];
                    functionDefinition = FunctionDefinition.fromJson(item);
                    functionDefinition.rowKeyForJobRunLookup = item.rowKey;
                    $scope.functionDefinitions.push(functionDefinition);
                }
            });
        }

        function getData() {
            getFunctionDefinitions();
        }

        function startPolling() {
            poll = $interval(function () {
                if (((new Date()) - lastPoll) > pollInterval) {
                    lastPoll = new Date();
                    getData();
                }
            }, 2000);
        }

        startPolling();
        $scope.$on('$destroy', function () {
            // Make sure that the interval is destroyed too
            if (poll) {
                $interval.cancel(poll);
                poll = undefined;
            }
        });
    }
);
