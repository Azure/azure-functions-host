angular.module('dashboard').controller('ErrorsController',
    function ErrorsController($rootScope, $scope) {
        $scope.errors = $rootScope.errors;
        $scope.warnings = $rootScope.warnings;

        $rootScope.errors = [];
        $rootScope.warnings = [];
    }
);
