angular.module('dashboard').controller('ErrorsController',
    function ErrorsController($rootScope, $scope) {
        if ($scope.errors) {
            for (var error in $rootScope.errors) {
                $scope.errors.push($rootScope.errors[error]);
            }
        } else {
            $scope.errors = $rootScope.errors;
        }

        if ($scope.warnings) {
            for (var warning in $rootScope.warnings) {
                $scope.warnings.push($rootScope.warnings[warning]);
            }
        } else {
            $scope.warnings = $rootScope.warnings;
        }

        $rootScope.errors = [];
        $rootScope.warnings = [];
    }
);
