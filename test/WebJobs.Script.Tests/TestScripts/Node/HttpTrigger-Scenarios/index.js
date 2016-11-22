var util = require('util');

module.exports = function (context, req) {
    var scenario = req.body.scenario;

    switch (scenario) {
        case 'echo':
            context.res = req.body.value;
            context.done();
            break;
        
        case 'buffer':
            context.res.send(Buffer.from('0001', 'hex'));
            break;

        default: 
            context.sendStatus(400);
            break;
    }
}