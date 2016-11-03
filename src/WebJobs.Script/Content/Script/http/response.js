// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

module.exports = (context) => {
    var res = {
        headers: {},

        end: (body) => {
            if (body !== undefined) {
                if (Buffer.isBuffer(body) && !res.get('Content-Type'))
                    res.type('application/octet-stream');
                res.body = body;
            }
            context.done();
            return res;
        },

        status: (statusCode) => {
            res.statusCode = statusCode;
            return res;
        },

        set: (field, val) => {
            res.headers[field] = val;
            return res;
        },

        sendStatus: (statusCode) => {
            return res.status(statusCode)
                .end();
        },

        type: (type) => {
            return res.set('Content-Type', type);
        },

        json: (body) => {
            return res.type('application/json')
                .send(body);
        },

        get: (field) => {
            return res.headers[field]
        }
    };

    res.send = res.end;
    res.header = res.set;

    return res;
};
