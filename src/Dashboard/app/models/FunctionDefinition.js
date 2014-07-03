angular.module('dashboard').factory('FunctionDefinition', function (stringUtils) {
    function FunctionDefinition() {

    }

    FunctionDefinition.fromJson = function (item) {
        var model = new FunctionDefinition();
        model.functionId = item.functionId;
        model.functionFullName = item.functionFullName;
        model.functionName = item.functionName;
        model.successCount = item.successCount;
        model.failedCount = item.failedCount;
        model.isRunning = !!item.isRunning;

        return model;
    };

    return FunctionDefinition;
});

