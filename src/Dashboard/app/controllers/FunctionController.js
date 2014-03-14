angular.module('dashboard').controller('FunctionController',
    function ($scope, $routeParams, $interval, $http, stringUtils, FunctionInvocationSummary, api, urls, isUsingSdk) {
        var poll,
            pollInterval = 10 * 1000,
            lastPoll = 0,
            functionName = $routeParams.functionFullName;

        isUsingSdk.setUsing($scope);
        $scope.functionFullName = functionName;

        $scope.breadcrumbs = [{
            url: urls.functions(),
            title: 'Functions'
        }];

        if ($scope._sdkNotConfigured) {
            return;
        }

        $scope.invocations = {
            endpoint: api.sdk.invocationsByFunction(functionName)
        };

        function getData() {
            $scope.$broadcast('invocations:poll');
            lastPoll = new Date();
        }

        getData();
        poll = $interval(function () {
            if (((new Date()) - lastPoll) > pollInterval) {
                getData();
            }
            $scope.$broadcast('invocations:updateTiming');
        }, 2000);

        $scope.$on('$destroy', function () {
            // Make sure that the interval is destroyed too
            if (poll) {
                $interval.cancel(poll);
                poll = undefined;
            }
        });
    }
);
