module.exports = function (context, input) {
    var logEntry = {
        message: 'Node.js manually triggered function called!',
        input: input
    };
    context.log(logEntry);
    context.log('Mathew Charles');
    context.log(null);
    context.log(1234);
    context.log(true);

    context.done();
}