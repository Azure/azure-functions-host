// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

const http = require('node:http');

const endpoint = process.env.FUNCTIONS_HTTP_ENDPOINT || '127.0.0.1:3000';
const [hostname, port] = endpoint.split(':');
let activeStreams = 0;

const server = http.createServer((request, response) => {
    const url = new URL(request.url, 'http://localhost');

    if (url.pathname === '/events') {
        response.writeHead(200, {
            'Content-Type': 'text/event-stream',
            'Cache-Control': 'no-cache',
        });
        response.flushHeaders();
        response.write('data: connected\n\n');
        activeStreams++;

        const timer = setInterval(() => {
            response.write(`data: ${new Date().toISOString()}\n\n`);
        }, 1000);
        response.on('close', () => {
            clearInterval(timer);
            activeStreams--;
        });
        return;
    }

    if (url.pathname === '/streams') {
        response.writeHead(200, { 'Content-Type': 'application/json' });
        response.end(JSON.stringify({ activeStreams }));
        return;
    }

    const chunks = [];
    request.on('data', (chunk) => chunks.push(chunk));
    request.on('end', () => {
        response.writeHead(200, { 'Content-Type': 'application/json' });
        response.end(JSON.stringify({
            message: 'Hello from an Azure Web Function!',
            method: request.method,
            path: request.url,
            body: Buffer.concat(chunks).toString(),
            requestId: request.headers['x-azure-functions-invocationid'] || null,
        }));
    });
});

server.listen(Number(port), hostname, () => {
    console.log(`Web function listening on http://${hostname}:${port}`);
});
