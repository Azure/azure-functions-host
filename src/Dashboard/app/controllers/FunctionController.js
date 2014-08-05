angular.module('dashboard').controller('FunctionController',
    function ($rootScope, $scope, $routeParams, $interval, $http, $location, stringUtils, FunctionInvocationSummary, api, urls, isUsingSdk) {
        var poll,
            pollInterval = 10 * 1000,
            lastPoll = 0,
            functionId = $routeParams.functionId,
            functionDataUrl = api.sdk.functionDefinition(functionId);

        isUsingSdk.setUsing($scope);
        $scope.functionId = functionId;

        $scope.breadcrumbs = [{
            url: urls.functions(),
            title: 'Functions'
        }];

        if ($scope._sdkNotConfigured) {
            return;
        }

        $scope.invocations = {
            endpoint: api.sdk.invocationsByFunction(functionId)
        };

        function loadFunctionDetails() {
            return $http.get(functionDataUrl)
                .success(function (res) {
                    $scope.functionName = res.functionName;
                }).error(function (res, code, data) {
                    if (code === 404) {
                        $rootScope.errors.push('Invalid function');
                    } else {
                        $rootScope.errors.push('Invalid function (Error code: ' + code + ')');
                    }

                    $location.url('/functions');
                });
        }

        function getData() {
            loadFunctionDetails().then(function () {
                $scope.$broadcast('invocations:poll');
            });
            lastPoll = new Date();
        }

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
