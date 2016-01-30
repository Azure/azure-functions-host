module.exports = function (input, context) {
    var json = JSON.stringify(input);
    context.log("Node.js script processed queue message '" + json + "'");

    context.done(null, json);
}