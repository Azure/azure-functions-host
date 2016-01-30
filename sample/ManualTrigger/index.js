module.exports = function (input, context) {
    context.log('Node.js manually triggered function called with input ' + input);
    context.done();
}