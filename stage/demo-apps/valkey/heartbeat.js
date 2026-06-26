// Stage demo (card #440): a long-running, non-HTTP supervised process for the
// internal-service type. Stands in for a key-value store the operator runs on the
// box (Collabhost supervises it but emits no proxy route). The periodic stdout
// line exercises the log ring buffer on the app detail page. The service name is
// passed as the first argument.
const name = process.argv[2] || "service";

console.log(`${name}: starting (Collabhost stage internal-service demo)`);

let beat = 0;
setInterval(() => {
  beat += 1;
  console.log(`${name}: heartbeat ${beat} — ${new Date().toISOString()}`);
}, 5000);
