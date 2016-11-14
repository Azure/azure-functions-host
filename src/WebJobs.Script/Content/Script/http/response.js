// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

module.exports = (context) => {
    var res = {
        headers: {},
        
        // node httpResponse methods
        setHeader: (field, val) => {
            res.headers[field.toLowerCase()] = val;
        },

        getHeader: (field) => {
            return res.headers[field.toLowerCase()];
        },

        removeHeader: (field) => {
            delete res.headers[field.toLowerCase()];
        },

        end: (body) => {
            if (body !== undefined) {
                res.body = body;
            }
            setContentType(res);
            context.done();
            return res;
        },

        // express methods
        status: (statusCode) => {
            res.statusCode = statusCode;
            return res;
        },

        set: (field, val) => {
            res.setHeader(field, val);
            return res;
        },

        sendStatus: (statusCode) => {
            return res.status(statusCode)
                .end();
        },

        type: (type) => {
            return res.set('content-type', type);
        },

        json: (body) => {
            return res.type('application/json')
                .send(body);
        },

        get: (field) => {
            return res.getHeader(field);
        }
    };

    res.send = res.end;
    res.header = res.set;

    return res;
};

function setContentType(res) {
    if (res.body !== undefined) {
        if (res.get('content-type')) {
            // use user defined content type, if exists
            return;
        }

        if (Buffer.isBuffer(res.body)) {
            res.type('application/octet-stream');
        }
    }
}
