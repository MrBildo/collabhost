#!/usr/bin/env bash
# stage-standup.sh -- idempotent box provisioning for the Collabhost STAGE
# instance (card #443). Run as root from a staging dir that holds the config
# files. Creates users/groups/dir-tree/ownership and installs the unit,
# appsettings, privop, dispatcher, and sudoers. Does NOT install secrets
# (admin-key drop-in, authorized_keys) -- those are installed separately so the
# generated admin key can be captured.
#
#   sudo ./stage-standup.sh /tmp/stage-standup [/path/to/repo/stage]
#
# Pass the repo `stage/` dir as the 2nd arg to (re)lay Remy's deploy kit into
# /opt/collabhost-stage/deploy with the root:root ownership the dispatcher's
# tamper check requires. This is the SINGLE place that owns kit-install -- do
# NOT copy the kit in out-of-band (that has drifted the ownership repeatedly).
set -euo pipefail
SRC="${1:?usage: stage-standup.sh <staging-dir> [<repo-stage-dir>]}"
KIT="${2:-}"   # repo stage/ dir; when given, (re)lay + chown the deploy kit
[ "$(id -u)" = "0" ] || { echo "must run as root"; exit 1; }

SVC=collabhost-stage          # service user/group (nologin, no home)
DEP=stage-deploy              # deploy principal (forced-command SSH only)

echo "== users/groups =="
getent group  "$SVC" >/dev/null || groupadd --system "$SVC"
getent passwd "$SVC" >/dev/null || useradd --system --gid "$SVC" \
  --no-create-home --home-dir /home/"$SVC" --shell /usr/sbin/nologin "$SVC"
getent passwd "$DEP" >/dev/null || useradd --system --create-home \
  --home-dir /home/"$DEP" --shell /bin/bash "$DEP"
passwd -l "$DEP" >/dev/null 2>&1 || true   # no password login; SSH key-only

echo "== dir tree + ownership =="
# Service-owned trees (the service writes data; the deployer reaches them only via privop).
install -d -m 0755 -o root  -g root  /opt/collabhost-stage
install -d -m 0750 -o "$SVC" -g "$SVC" /opt/collabhost-stage/bin /opt/collabhost-stage/wwwroot
install -d -m 0755 -o root  -g root  /opt/collabhost-stage/deploy
install -d -m 0750 -o "$SVC" -g "$SVC" \
  /var/lib/collabhost-stage \
  /var/lib/collabhost-stage/data \
  /var/lib/collabhost-stage/user-types \
  /var/lib/collabhost-stage/caddy \
  /var/lib/collabhost-stage/dotnet-bundle \
  /var/lib/collabhost-stage/app-data \
  /var/log/collabhost-stage \
  /srv/stage
install -d -m 0755 -o root -g root /etc/collabhost-stage
# Deployer-owned working + audit areas.
install -d -m 0750 -o "$DEP" -g "$DEP" /home/"$DEP"/build /var/log/stage-deploy

echo "== install config files (line-endings sanitised) =="
sanitize() { sed 's/\r$//' "$SRC/$1"; }   # tolerate CRLF from a Windows author
sanitize collabhost-stage.service > /etc/systemd/system/collabhost-stage.service
chmod 0644 /etc/systemd/system/collabhost-stage.service; chown root:root /etc/systemd/system/collabhost-stage.service
sanitize stage-appsettings.json > /etc/collabhost-stage/appsettings.json
chmod 0640 /etc/collabhost-stage/appsettings.json; chown root:"$SVC" /etc/collabhost-stage/appsettings.json
sanitize stage-deploy-dispatch > /usr/local/bin/stage-deploy-dispatch
chmod 0755 /usr/local/bin/stage-deploy-dispatch; chown root:root /usr/local/bin/stage-deploy-dispatch
# NB: stage-privop is installed in the "deploy kit" step below (it lives in the
# root-owned deploy dir and shares that dir's root:root invariant).

echo "== sudoers (validated) =="
sanitize stage-deploy.sudoers > /etc/sudoers.d/stage-deploy.tmp
chmod 0440 /etc/sudoers.d/stage-deploy.tmp; chown root:root /etc/sudoers.d/stage-deploy.tmp
if visudo -c -f /etc/sudoers.d/stage-deploy.tmp; then
  mv /etc/sudoers.d/stage-deploy.tmp /etc/sudoers.d/stage-deploy
  visudo -c >/dev/null && echo "sudoers OK"
else
  rm -f /etc/sudoers.d/stage-deploy.tmp; echo "SUDOERS INVALID -- aborted"; exit 1
fi

echo "== deploy kit -> /opt/collabhost-stage/deploy (root:root hard invariant) =="
# The dispatcher execs deploy-stage.sh ONLY if it is uid 0 and not group/other-
# writable; a non-root owner makes it refuse EVERY deploy ("failed the tamper
# check"). The whole deploy dir -- Remy's kit AND stage-privop -- must be
# root-owned. This step is the SINGLE place that enforces it. Pass the repo
# stage/ dir (2nd arg) to (re)lay the kit; re-run after any kit update instead
# of copying in out-of-band (out-of-band lays have drifted the ownership).
if [ -n "$KIT" ]; then
  [ -f "$KIT/deploy-stage.sh" ] || { echo "kit '$KIT' has no deploy-stage.sh -- pass the repo stage/ dir"; exit 1; }
  if command -v rsync >/dev/null 2>&1; then
    rsync -a --exclude='/box/' "$KIT"/ /opt/collabhost-stage/deploy/   # box/ = these stand-up artifacts, not the runtime kit
  else
    ( cd "$KIT" && tar --exclude=./box -cf - . ) | ( cd /opt/collabhost-stage/deploy && tar -xf - )
  fi
else
  echo "  (no <repo-stage-dir> arg -- kit not (re)laid; enforcing ownership only)"
fi
sanitize stage-privop > /opt/collabhost-stage/deploy/stage-privop   # box-side privilege helper
chown -R root:root /opt/collabhost-stage/deploy
find /opt/collabhost-stage/deploy -type d -exec chmod 0755 {} +
find /opt/collabhost-stage/deploy -type f -exec chmod 0644 {} +
find /opt/collabhost-stage/deploy -type f -name '*.sh' -exec chmod 0755 {} +
chmod 0755 /opt/collabhost-stage/deploy/stage-privop
echo "deploy kit: root:root enforced"

echo "== .ssh skeleton for $DEP (authorized_keys installed separately) =="
install -d -m 0700 -o "$DEP" -g "$DEP" /home/"$DEP"/.ssh

echo "== daemon-reload =="
systemctl daemon-reload
echo "stage-standup: done (service NOT started -- no binary yet; awaiting first deploy)"
