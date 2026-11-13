'use strict';

const express = require('express');

const app = express();
const port = Number(process.env.PORT) || 3000;
const startedAt = new Date().toISOString();

// In-memory data is per process: it is lost on restart and is not shared across scaled-out instances.
const todos = new Map();
let nextId = 1;

app.use(express.json());
app.use(express.static(`${__dirname}/public`));

app.get('/api/info', (req, res) => {
    res.json({
        framework: `express ${require('express/package.json').version}`,
        node: process.version,
        pid: process.pid,
        startedAt,
        listeningOn: `127.0.0.1:${port}`,
        functionsHttpEndpoint: process.env.FUNCTIONS_HTTP_ENDPOINT ?? null
    });
});

app.get('/api/headers', (req, res) => {
    res.json({
        method: req.method,
        originalUrl: req.originalUrl,
        headers: req.headers
    });
});

app.get('/api/todos', (req, res) => {
    res.json([...todos.values()]);
});

app.get('/api/todos/:id', (req, res) => {
    const todo = todos.get(Number(req.params.id));
    if (!todo) {
        return res.status(404).json({ error: 'Todo not found' });
    }

    res.json(todo);
});

app.post('/api/todos', (req, res) => {
    const title = typeof req.body?.title === 'string' ? req.body.title.trim() : '';
    if (!title) {
        return res.status(400).json({ error: 'A non-empty "title" is required' });
    }

    const todo = { id: nextId++, title, done: false };
    todos.set(todo.id, todo);
    res.status(201).location(`/api/todos/${todo.id}`).json(todo);
});

app.patch('/api/todos/:id', (req, res) => {
    const todo = todos.get(Number(req.params.id));
    if (!todo) {
        return res.status(404).json({ error: 'Todo not found' });
    }

    if (typeof req.body?.done === 'boolean') {
        todo.done = req.body.done;
    }

    res.json(todo);
});

app.delete('/api/todos/:id', (req, res) => {
    if (!todos.delete(Number(req.params.id))) {
        return res.status(404).json({ error: 'Todo not found' });
    }

    res.status(204).end();
});

app.get('/old-home', (req, res) => {
    res.redirect(302, '/');
});

app.get('/api/error', () => {
    throw new Error('Intentional error from the Express sample');
});

app.use((req, res) => {
    res.status(404).json({ error: `No route for ${req.method} ${req.path}` });
});

app.use((err, req, res, next) => {
    console.error(err);
    res.status(500).json({ error: err.message });
});

app.listen(port, '127.0.0.1', () => {
    console.log(`Express sample listening on http://127.0.0.1:${port}`);
});
