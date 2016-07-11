module.exports = function (context, req) {
    try {
        var countername = req.parameters.countername;
        var counter = context.bindings.counters.filter(function (c) { return c.RowKey === countername; })[0];
        context.res = {
            status: 200,
            body: counter.Value
        }

    } catch (e) {
        context.res = {
            status: 400,
            body: "Error: The counter is not initialized."
        };
    }

    context.res.headers = {
        'Content-Type': 'text/plain'
    };

    context.done();
}
