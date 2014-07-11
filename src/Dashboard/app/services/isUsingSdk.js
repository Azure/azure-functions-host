angular.module('dashboard').service('isUsingSdk', function ($rootScope, $http, api) {

    function isUsing(scope) {
        return scope._usingSdk === true;
    }

    function setUsing(scope) {
        if (scope) {
            scope._usingSdk = true;
        } else {
            $rootScope._usingSdk = true;
        }
    }

    function setNotUsing(scope) {
        scope._usingSdk = false;
    }

    function findOut(scope) {
        if (isUsing(scope)) {
            // this (or other) service already found out.
            return;
        }
        if (!$rootScope._sdkNotConfigured || $rootScope._sdkNotConfigured.connectionStringState !== 'missing') {
            // assume using if connectionString is not missing
            setUsing();
            return;
        }
        $http.get(api.kudu.jobs()).then(function (res) {
            var ix, len = res.data.length;
            for (ix = 0; ix !== len; ++ix) {
                if (res.data[ix].using_sdk) {
                    setUsing();
                    return;
                }
            }
            setNotUsing(scope);
        });
    }

    return {
        isUsing: isUsing,
        setUsing: setUsing,
        setNotUsing: setNotUsing,
        findOut: findOut
    };
});
