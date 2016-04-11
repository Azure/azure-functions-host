module.exports = function (context, scenario) {
    context.log("Running scenario '%s'", scenario);

    if (scenario == 'doubleDone') {
        context.done();
        context.done();
    }
    else {
        context.done();
    }
}