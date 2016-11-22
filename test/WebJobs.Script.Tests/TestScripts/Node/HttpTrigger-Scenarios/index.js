var util = require('util');

module.exports = function (context, req) {
    var scenario = req.body.scenario;

    if (scenario == "echo") {
        context.res = req.body.value;
    }
    else if (scenario == "xmlobjectsingleproperty") {
        context.res = {
            body: { name: "Fabio" },
            headers: {
                'Content-Type': 'text/xml'
            }
        }
    }
    else if (scenario == "xmlobjectmultipleproperties") {
        context.res = {
            body: { name: "Fabio", lastname: "Cavalcante" },
            headers: {
                'Content-Type': 'text/xml'
            }
        }
    }
    else {
        context.res = {
            status: 400
        };
    }

    context.done();
}