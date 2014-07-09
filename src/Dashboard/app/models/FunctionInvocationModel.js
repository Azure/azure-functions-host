angular.module('dashboard').factory('FunctionInvocationModel', function (stringUtils, FunctionInvocationSummary) {
    function FunctionInvocationModel() {

    }

    FunctionInvocationModel.fromJson = function(item) {
        var ix, len;
        var model = new FunctionInvocationModel();
        model.invocation = FunctionInvocationSummary.fromJson(item.invocation);
        if (item.ancestor) {
            model.ancestor = FunctionInvocationSummary.fromJson(item.ancestor);
        }
        model.trigger = item.trigger;
        model.isAborted = !!item.isAborted;
        model.parameters = [];
        if (item.parameters) {
            for (ix = 0, len = item.parameters.length; ix !== len; ++ix) {
                model.parameters.push(new FunctionParameter(item.parameters[ix]));
            }
        }

        return model;
    };

    function FunctionParameter(item) {
        this.name = item.name;
        this.extendedBlobModel = item.extendedBlobModel;
        this.argInvokeString = item.argInvokeString;
        this.status = item.status;
    }

    FunctionParameter.prototype.isParamOwnedSomeoneElse = function () {
        return this.extendedBlobModel
             && !this.extendedBlobModel.isBlobMissing
             && this.extendedBlobModel.isOutput && !this.extendedBlobModel.isBlobOwnedByCurrentFunctionInstance;
    };
    FunctionParameter.prototype.isParamOwnedAnotherFunction = function() {
        return this.extendedBlobModel
            && !this.extendedBlobModel.isBlobMissing
            && this.extendedBlobModel.isOutput && !this.extendedBlobModel.isBlobOwnedByCurrentFunctionInstance
            && this.extendedBlobModel.ownerId
            && this.extendedBlobModel.ownerId !== '00000000-0000-0000-0000-000000000000';
    };

    return FunctionInvocationModel;
});
