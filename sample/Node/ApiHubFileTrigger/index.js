module.exports = function (context, input) {
    context.log('Node.js ApiHub trigger function processed ', input);
    context.done(null, input);
}