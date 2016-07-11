var util = require('util');

module.exports = function (context, scenario) {
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
    else {
        // throw if the scenario didn't match
        throw new Error(util.format("The scenario '%s' did not match any known scenario.", scenario));
    }
}