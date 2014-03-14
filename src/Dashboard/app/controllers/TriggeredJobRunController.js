angular.module('dashboard').controller('TriggeredJobRunController',
    function ($scope, $routeParams, $interval, $http, stringUtils, FunctionInvocationSummary, JobRun, JobDefinition, urls, api, isUsingSdk) {
        var poll,
            pollInterval = 10 * 1000,
            lastPoll = 0,
            jobName = $routeParams.jobName,
            jobType = 'triggered',
            job,
            runId = $routeParams.runId,
            jobRunUrl = api.kudu.jobRun(jobName, runId);
        $scope.jobName = jobName;
        $scope.runId = runId;
        $scope.nonFinalInvocations = {};

        $scope.breadcrumbs = [{
            url: urls.jobs(),
            title: 'WebJobs'
        }, {
            url: urls.job(jobType, jobName),
            title: jobName
        }
        ];

        $scope.invocations = {
            endpoint: api.sdk.functionsInJob('triggered', jobName, runId),
        };

        function getJobRunDetails() {
            return $http.get(jobRunUrl).then(function (res) {
                var run = res.data,
                    jobRun = new JobRun(run.id, run.status, run.start_time, run.end_time, run.duration, run.output_url, run.error_url);
                $scope.jobRun = jobRun;
            });
        }

        function checkUsingSdk() {
            if (job) {
                return;
            }
            if (isUsingSdk.isUsing($scope)) {
                return;
            }
            $http.get(api.kudu.job('triggered', $routeParams.jobName)).then(function (res) {
                if (res.data.using_sdk) {
                    isUsingSdk.setUsing();
                } else {
                    isUsingSdk.setNotUsing($scope);
                }
            });
        }
        checkUsingSdk();


        function getData() {
            lastPoll = new Date();
            $scope.jobRun = $scope.jobRun || {};
            // TODO: look up all antares WebJobs statuses that are "not running"
            if ($scope.jobRun.status === 'Success') {
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
