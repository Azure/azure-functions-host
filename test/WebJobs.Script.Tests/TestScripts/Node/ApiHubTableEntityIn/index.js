module.exports = function (context, input) {
    if (context.bindings.entity.Id != input.id)
    {
        throw "Expected Id to be bound.";
    }
    context.log("TestResult:", context.bindings.entity.Id);
    context.done();
}