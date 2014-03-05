angular.module('dashboard').factory('FunctionDefinition', function (stringUtils) {
    function FunctionDefinition() {

    }

    FunctionDefinition.fromJson = function (item) {
        var model = new FunctionDefinition();
        model.functionFullName = item.functionFullName;
        model.functionName = item.functionName;
        model.successCount = item.successCount;
        model.failedCount = item.failedCount;
        model.isRunning = !!item.isRunning;
        model.isOldHost = !!item.isOldHost;
        model.lastStartTime = item.lastStartTime ? stringUtils.toDateTime(item.lastStartTime) : null;
        model.lastStartTimeString = model.lastStartTime ?
            stringUtils.formatDateTime(model.lastStartTime) : 'never ran';

        return model;
    };

    return FunctionDefinition;
});

