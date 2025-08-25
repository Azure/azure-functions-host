module.exports = function (context, trigger) {
    context.log("Trigger: " + trigger);
    context.bindings.output = trigger + "-completed";
    context.bindings.completed = context.bindings.output;
    context.done();
}