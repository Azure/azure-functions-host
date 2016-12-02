var util = require('util');

module.exports = function (context, req) {
    if (req.headers.return) {
        context.res = req.body;
        context.done();
    } else {
        var sendFunc = req.headers.raw ? 'raw' : 'send';
        context.res.type(req.headers.type)[sendFunc](req.body);
    }
}