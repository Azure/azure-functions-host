module.exports = function (context, input) {
    context.log('Node.js manually triggered function called with input', input);
    context.done();
}