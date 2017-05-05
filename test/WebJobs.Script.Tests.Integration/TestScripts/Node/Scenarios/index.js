var util = require('util');
var assert = require('assert');

﻿module.exports = function (context, input) {
    var scenario = input.scenario;
    context.log("Running scenario '%s'", scenario);

    if (scenario === 'nextTick') {
        process.nextTick(function () {
            // without the workaround this would hang
            context.done();
        });
    }
    else if (scenario === 'promiseResolve') {
        Promise.resolve().then(() => context.done());
    }
    else if (scenario === 'promiseApiResolves') {
        return Promise.resolve();
    }
    else if (scenario === 'promiseApiRejects') {
        return Promise.reject('reject');
    }
    else if (scenario === 'randGuid') {
        context.bindings.blob = input.value;
        context.done();
    }
    else if (scenario === 'logging') {
        var logEntry = {
            message: 'This is a test',
            version: process.version,
            input: input.input
        };
        context.log(logEntry);
        context.log('Mathew Charles');
        context.log(null);
        context.log(1234);
        context.log(true);

        context.log('loglevel default');
        context.log.info('loglevel info');
        context.log.verbose('loglevel verbose');
        context.log.warn('loglevel warn');
        context.log.error('loglevel error');

        context.done();
    }
    else if (scenario === 'consoleLog') {
        console.log("console.log");
        context.done();
    }
    else if (scenario === 'bindingData') {
        var bindingData = context.bindingData;

        assert(context.bindingData);
        assert.equal(context.invocationId, context.bindingData.invocationId);

        // verify all context properties are camel cased
        for (var key in context) {
            assert(isLowerCase(key[0]), "Expected lower case: " + key);
        }

        // verify all binding data properties are camel cased
        for (var key in context.bindingData)
        {
            assert(isLowerCase(key[0]), "Expected lower case: " + key);
        }

        // verify that system properties were removed
        assert(!context._inputs);
        assert(!context._entryPoint);

        context.done();
    }
    else if (scenario === 'bindingContainsFunctions') {
        context.bindings.blob = {
            func: () => { },
            nested: {
                func: () => { }
            },
            array: [
                { func: () => { } }
            ],
            value: "value"
        };
        context.done();
    }
    else if (scenario === "functionExecutionContext") {
        context.log.info("FunctionName:" + context.executionContext.functionName);
        context.log.info("FunctionDirectory:" + context.executionContext.functionDirectory);
        context.log.info("InvocationId:" + context.executionContext.invocationId);
        context.done();
    }
    else {
        // throw if the scenario didn't match
        throw new Error(util.format("The scenario '%s' did not match any known scenario.", scenario));
    }
}

function isLowerCase(c) {
    return c.toLowerCase() === c;
}