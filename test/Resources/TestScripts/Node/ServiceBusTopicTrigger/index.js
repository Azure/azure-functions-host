module.exports = function (context, trigger) {
    context.log("Trigger: " + trigger);
    context.bindings.completed = trigger + "-completed";
    context.done();
}