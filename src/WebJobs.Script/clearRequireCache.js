// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

return function (context, callback) {
    Object.keys(require.cache).forEach(function (key) {
        delete require.cache[key];
    });
    callback();
}