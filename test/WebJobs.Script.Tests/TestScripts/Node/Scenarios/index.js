module.exports = function (context, input) {
    var scenario = input.scenario;
    context.log("Running scenario '%s'", scenario);

    if (scenario == 'doubleDone') {
        context.done();
        context.done();
    }
    else if (scenario == 'randGuid') {
        context.bindings.blob = input.value;
        context.done();
    }
    else {
        context.done();
    }
}