#!/usr/bin/env bash
# Generate the stage admin key (ULID) and install it: a 0600 systemd drop-in for
# the service, plus a stage-deploy-readable copy for the seed (disposable-stage
# secret -- NOT a prod credential). Idempotent only if re-keying is intended;
# re-running rotates the key, so guard: keep the existing key if present.
set -euo pipefail
DROPIN=/etc/systemd/system/collabhost-stage.service.d/override.conf
SEEDKEY=/home/stage-deploy/.stage-admin-key

if [ -f "$DROPIN" ] && grep -q COLLABHOST_ADMIN_KEY "$DROPIN"; then
  echo "admin key already present in $DROPIN -- leaving as-is (re-key is a deliberate runbook step)"
  exit 0
fi

ULID=$(python3 - <<'PY'
import os,time
A="0123456789ABCDEFGHJKMNPQRSTVWXYZ"
ts=int(time.time()*1000); rnd=int.from_bytes(os.urandom(10),'big')
n=(ts<<80)|rnd
print(''.join(A[(n>>(5*(25-i)))&31] for i in range(26)))
PY
)

install -d -m 0755 -o root -g root /etc/systemd/system/collabhost-stage.service.d
umask 077
printf '[Service]\nEnvironment=COLLABHOST_ADMIN_KEY=%s\n' "$ULID" > "$DROPIN"
chmod 0600 "$DROPIN"; chown root:root "$DROPIN"

printf '%s' "$ULID" > "$SEEDKEY"
chmod 0600 "$SEEDKEY"; chown stage-deploy:stage-deploy "$SEEDKEY"

systemctl daemon-reload
echo "admin key installed (hint: ${ULID:0:6}...)  service=$DROPIN(0600 root)  seed=$SEEDKEY(0600 stage-deploy)"
