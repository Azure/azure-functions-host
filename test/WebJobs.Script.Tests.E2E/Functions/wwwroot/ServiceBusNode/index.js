module.exports = function (context, req) {
    var counters = JSON.parse(process.env.WEBSITE_COUNTERS_APP);
    var connections = {
        connections: counters.connections,
        connectionLimit: counters.connectionLimit
    };
    context.bindings.outputSbMsg = "test";
    context.done(null, connections);
};
