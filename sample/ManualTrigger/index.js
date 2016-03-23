module.exports = function (context, input) {
    context.log('Node.js manually triggered function called with input %s', input);
    context.done();
}