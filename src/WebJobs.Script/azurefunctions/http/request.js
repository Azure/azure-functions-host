// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

module.exports = (context) => {
    var req = context.req;
    req.get = (field) => req.headers[field.toLowerCase()];
    return req;
};