angular.module('dashboard').controller('TriggeredJobController',
  function ($scope, $routeParams, $interval, $http, JobRun, JobDefinition, api, urls, isUsingSdk) {
      var poll,
          pollInterval = 10 * 1000,
          lastPoll = 0;

      isUsingSdk.findOut($scope);

      $scope.jobName = $routeParams.jobName;

      $scope.breadcrumbs = [{
          url: urls.jobs(),
          title: 'WebJobs'
      }      ];

      function getData() {
          getInitialJobData();
          getTriggeringHistory();
      }

      function getInitialJobData() {
          if ($scope.job) {
              return;
          }

          $http.get(api.kudu.job('triggered', $routeParams.jobName)).then(function (res) {
              $scope.job = new JobDefinition(res.data);
          });
      }

      function getTriggeringHistory() {
          var run,
              url = api.kudu.jobHistory("triggered", $routeParams.jobName),
              params = {};

          $scope.runs = $scope.runs || [];

          if ($scope.runs.length > 0) {
              params.lastKnownId = $scope.runs[0].id;
          }

          $http.get(url, { params: params }).then(function (res) {
              var lastKnownId,
                  len = res.data.runs.length,
                  ix,
                  newRuns = [],
                  etag = res.headers().etag;
              if ($scope.etag === etag) {
                  return;
              }
              $scope.etag = etag;
              if (len === 0) {
                  return;
              }
              // ensure orderby id DESC
              res.data.runs.sort(function (a, b) {
                  return a.id < b.id ? 1 : a.id > b.id ? -1 : 0;
              });
              lastKnownId = $scope.runs.length > 0 ? $scope.runs[0].id : "-1";
              for (ix = 0; ix !== len; ++ix) {
                  run = res.data.runs[ix];
                  if (run.id < lastKnownId) {
                      break;
                  }
                  newRuns.push(new JobRun(run.id, run.status, run.start_time, run.end_time, run.duration, run.output_url, run.error_url));
                  if (run.id === lastKnownId) {
                      $scope.runs.splice(0, 1);
                      break;
                  }
              }
              console.log("found " + newRuns.length + " new runs.");
              for (ix = newRuns.length - 1; ix !== -1; --ix) {
                  $scope.runs.unshift(newRuns[ix]);
              }
          });
      }

      function updateTiming() {
          var ix, len = $scope.runs.length;
          for (ix = 0; ix !== len; ++ix) {
              $scope.runs[ix].updateTimingStrings();
          }
      }
      getData();
      poll = $interval(function () {
          if (((new Date()) - lastPoll) > pollInterval) {
              lastPoll = new Date();
              getData();
          }
          updateTiming();
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
