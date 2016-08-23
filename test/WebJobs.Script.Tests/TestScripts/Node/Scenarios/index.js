var util = require('util');

﻿module.exports = function (context, input) {
    var scenario = input.scenario;
    context.log("Running scenario '%s'", scenario);

    if (scenario == 'doubleDone') {
        context.done();
        context.done();
    }
    else if (scenario == 'nextTick') {
        process.nextTick(function () {
            // without the workaround this would hang
            context.done();
        });
    }
    else if (scenario == 'randGuid') {
        context.bindings.blob = input.value;
        context.done();
    }
    else if (scenario == 'logging') {
        var logEntry = {
            message: 'This is a test',
            input: input.input
        };
        context.log(logEntry);
        context.log('Mathew Charles');
        context.log(null);
        context.log(1234);
        context.log(true);

        context.done();
    }
    else {
        // throw if the scenario didn't match
        throw new Error(util.format("The scenario '%s' did not match any known scenario.", scenario));
    }
}