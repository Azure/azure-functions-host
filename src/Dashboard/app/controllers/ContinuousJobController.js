angular.module('dashboard').controller('ContinuousJobController',
    function ($scope, $routeParams, $interval, $http, stringUtils, FunctionInvocationSummary, JobDefinition, api, urls, isUsingSdk) {
        var poll,
            pollInterval = 10 * 1000,
            lastPoll = 0,
            jobName = $routeParams.jobName,
            jobRunUrl = api.kudu.job('continuous', jobName);

        $scope.jobName = jobName;
        $scope.nonFinalInvocations = {};

        $scope.breadcrumbs = [{
            url: urls.jobs(),
            title: 'WebJobs'
        }];

        $scope.invocations = {
            endpoint: api.sdk.functionsInJob('continuous', jobName),
        };

        function getJobRunDetails() {
            return $http.get(jobRunUrl).then(function (res) {
                $scope.job = new JobDefinition(res.data);
                if (!isUsingSdk.isUsing($scope)) {
                    if ($scope.job.usingSdk) {
                        isUsingSdk.setUsing();
                    } else {
                        isUsingSdk.setNotUsing($scope);
                    }
                }
            });
        }

        function getData() {
            lastPoll = new Date();
            // TODO: look up all antares WebJobs statuses that are "not running"
            if ($scope.job && $scope.job.isRunning()) {
                return;
            }
            getJobRunDetails();
            if (!$scope._sdkNotConfigured) {
                $scope.$broadcast('invocations:poll');
            }
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
