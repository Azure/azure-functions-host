// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

module.exports = (context) => {
    var res = {
        headers: {},
        body: undefined,

        end: (body) => {
            if (body !== undefined) {
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

        raw: (body) => {
            res.isRaw = true;
            return res.send(body);
        },

        get: (field) => {
            return res.headers[field]
        }
    };

    res.send = res.end;
    res.header = res.set;

    return res;
};
