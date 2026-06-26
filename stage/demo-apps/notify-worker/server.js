// Stage demo (card #440): a tiny Node.js HTTP service.
//
// Collabhost injects the listen port via PORT (the nodejs-app type's
// port-injection binding). Exposes "/" (served through the proxy route) and
// "/health" (the health-check binding). Seeded but left STOPPED in the demo set
// so the dashboard / app-list show a non-running row (status variety).
const http = require("node:http");

const port = Number(process.env.PORT) || 3000;

const server = http.createServer((req, res) => {
  if (req.url === "/health") {
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(JSON.stringify({ status: "healthy" }));
    return;
  }

  res.writeHead(200, { "Content-Type": "text/plain" });
  res.end("Notification Worker — running on Collabhost stage.\n");
});

server.listen(port, () => {
  console.log(`notify-worker listening on ${port}`);
});
