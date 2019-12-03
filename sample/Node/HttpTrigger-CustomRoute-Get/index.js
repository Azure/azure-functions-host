var util = require('util');

var catalog = [
    {
        id: '12ec126e-3208-4542-a4a0-30e65438832a',
        category: 'electronics'
    },
    {
        id: '4e2796ae-b865-4071-8a20-2a15cbaf856c',
        category: 'electronics'
    },
    {
        id: '52aaf427-45fb-49dd-9105-15bfb217db5e',
        category: 'housewares'
    }
];

module.exports = function (context, req) {
    context.log('Node.js HTTP trigger function processed a request. Name=%s', req.query.name);

    context.log('category: %s id: %s', context.bindingData.category || '<empty>', context.bindingData.id || '<empty>');

    var results = [];
    catalog.forEach(function (product) {
        if ((!req.params.id || (product.id == req.params.id)) &&
            (!req.params.category || (product.category == req.params.category))) {
            results.push(product);
        }
    });

    context.done(null, { body: results });
}
