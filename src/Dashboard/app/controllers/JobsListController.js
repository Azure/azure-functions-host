angular.module('dashboard').controller('JobsListController',
  function ($scope, $routeParams, $interval, $http, JobDefinition, api) {
      var poll,
          pollInterval = 60 * 1000,
          lastPoll = 0;

      function getData() {
          $scope.jobs = $scope.jobs || [];

          $http.get(api.kudu.jobs()).then(function (res) {
              var ix, len = res.data.length;
              $scope.jobs = [];

              for (ix = 0; ix !== len; ++ix) {
                  $scope.jobs.push(new JobDefinition(res.data[ix]));
              }
          });
      }

      function updateStrings() {
          var ix, len = $scope.jobs.length;
          for (ix = 0; ix !== len ; ++ix) {
              $scope.jobs[ix].updateStrings();
          }
      }
      getData();
      poll = $interval(function () {
          if (((new Date()) - lastPoll) > pollInterval) {
              lastPoll = new Date();
              getData();
          }
          updateStrings();
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
