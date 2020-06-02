const express = require("express");
const app = express();

app.use(express.json());

const PORT = process.env.FUNCTIONS_HTTPWORKER_PORT;

const server = app.listen(PORT, "localhost", () => {
    console.log(`Your port is ${PORT}`);
    const { address: host, port } = server.address();
    console.log(`Example app listening at http://${host}:${port}`);
});

app.get("/hello", (req, res) => {
    res.json("Hello World!");
});

app.post("/hello", (req, res) => {
    res.json({ value: req.body });
});