module.exports = function (context, timerInfo) {
    context.log(context.bindings.inputData);
    
    // Run for 30 seconds before closing. Test will timeout in 3 seconds but this cleans up.
    var stop = new Date().getTime() + 10000;
    while (new Date().getTime() <= stop) { }
    context.done();
}