// Stage demo: a tiny Node.js HTTP service (card #443).
//
// Collabhost injects the listen port via PORT (the nodejs-app type's port-injection
// binding). Exposes "/" (served through the proxy route) and "/health" (the
// health-check binding). No dependencies, so `npm install` is effectively instant.
const http = require("node:http");

const port = Number(process.env.PORT) || 3000;

const server = http.createServer((req, res) => {
  if (req.url === "/health") {
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(JSON.stringify({ status: "healthy" }));
    return;
  }

  res.writeHead(200, { "Content-Type": "text/plain" });
  res.end("Demo Node widget — running on Collabhost stage.\n");
});

server.listen(port, () => {
  console.log(`demo-node-widget listening on ${port}`);
});
