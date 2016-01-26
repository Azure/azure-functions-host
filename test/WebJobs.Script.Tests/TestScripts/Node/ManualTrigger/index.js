module.exports = function (context) {
    context.log('Node.js manually triggered function called with input ' + context.input);
    context.done();
}