module.exports = function (context, timerInfo) {
    var timeStamp = new Date().toISOString();
    context.log('Timer function ran! ', timeStamp);
    context.bindings.output = "From timer trigger: " + timeStamp;
    context.done();
}