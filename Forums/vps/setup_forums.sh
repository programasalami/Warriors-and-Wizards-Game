#!/bin/bash
# One-time (and re-runnable) setup of the NodeBB forums on the VPS (Ubuntu / Debian). Run as root:
#     bash setup_forums.sh forums.warriorsandwizards.com <admin user> <admin email> <admin password>
# Prerequisite: a DNS "A" record  forums  ->  104.152.50.196  (DNS-only / grey cloud in Cloudflare) so Let's Encrypt can verify the name.
# What it sets up:
#   * Node.js 20 (NodeSource), NodeBB (v3 branch) in /opt/nodebb, run by its own "nodebb" user as the systemd service "ww-forums",
#   * a Postgres role + database "nodebb" on the VPS's existing Postgres (its password is generated here and lives only in
#     /opt/nodebb/config.json),
#   * nginx serving https://<domain> (Let's Encrypt, auto-renewed) as a reverse proxy to NodeBB on 127.0.0.1:4567.
# Re-running upgrades NodeBB (git pull + ./nodebb upgrade) and keeps the data. Needs about 1 GB of RAM for NodeBB + its build.
set -e
DOMAIN="${1:?usage: bash setup_forums.sh <domain> <admin user> <admin email> <admin password>}"
ADMIN_USER="${2:?admin user missing}"
ADMIN_EMAIL="${3:?admin email missing}"
ADMIN_PASS="${4:?admin password missing}"
NODEBB_DIR=/opt/nodebb
NODEBB_BRANCH=v3.x
PG_DB=nodebb
PG_USER=nodebb

echo "== swap (insurance for the NodeBB build on a small box; only if the VPS has no swap yet)"
if [ -z "$(swapon --show --noheadings)" ]; then
    fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
    grep -q '^/swapfile' /etc/fstab || echo '/swapfile none swap sw 0 0' >> /etc/fstab
    echo "created a 2 GB swap file"
fi

echo "== packages"
apt-get update
apt-get install -y nginx certbot python3-certbot-nginx git build-essential curl ca-certificates
if ! command -v node >/dev/null 2>&1 || [ "$(node -v | cut -c2-3)" -lt 20 ]; then
    curl -fsSL https://deb.nodesource.com/setup_20.x | bash -
    apt-get install -y nodejs
fi
node -v; npm -v

echo "== nodebb user"
id -u nodebb >/dev/null 2>&1 || useradd --system --home "$NODEBB_DIR" --shell /usr/sbin/nologin nodebb

echo "== postgres role + database"
cd /var/lib/postgresql 2>/dev/null || cd /tmp     # psql as the postgres user cannot read /root ("could not change directory" is only that)
if [ -f "$NODEBB_DIR/config.json" ]; then
    PG_PASS=$(python3 -c "import json;print(json.load(open('$NODEBB_DIR/config.json'))['postgres']['password'])")
else
    PG_PASS=$(tr -dc 'A-Za-z0-9' </dev/urandom | head -c 32)
fi
runuser -u postgres -- psql -tc "SELECT 1 FROM pg_roles WHERE rolname='$PG_USER'" | grep -q 1 \
    || runuser -u postgres -- psql -c "CREATE ROLE $PG_USER LOGIN PASSWORD '$PG_PASS';"
runuser -u postgres -- psql -c "ALTER ROLE $PG_USER PASSWORD '$PG_PASS';"
runuser -u postgres -- psql -tc "SELECT 1 FROM pg_database WHERE datname='$PG_DB'" | grep -q 1 \
    || runuser -u postgres -- createdb -O "$PG_USER" "$PG_DB"

echo "== nodebb source"
if [ ! -d "$NODEBB_DIR/.git" ]; then
    git clone -b "$NODEBB_BRANCH" --depth 1 https://github.com/NodeBB/NodeBB.git "$NODEBB_DIR"
else
    chown -R nodebb:nodebb "$NODEBB_DIR"
    runuser -u nodebb -- git -C "$NODEBB_DIR" pull --ff-only     # as the owner: git refuses a folder owned by another user
fi
chown -R nodebb:nodebb "$NODEBB_DIR"

if [ ! -f "$NODEBB_DIR/config.json" ]; then
    echo "== first-time nodebb setup"
    SECRET=$(tr -dc 'A-Za-z0-9' </dev/urandom | head -c 48)
    SETUP_JSON=$(python3 - "$DOMAIN" "$SECRET" "$PG_PASS" "$ADMIN_USER" "$ADMIN_EMAIL" "$ADMIN_PASS" <<'PY'
import json, sys
d, secret, pg, au, ae, ap = sys.argv[1:]
print(json.dumps({
    "url": "https://" + d, "secret": secret, "database": "postgres",
    "postgres:host": "127.0.0.1", "postgres:port": 5432, "postgres:username": "nodebb", "postgres:password": pg, "postgres:database": "nodebb",
    "admin:username": au, "admin:password": ap, "admin:password:confirm": ap, "admin:email": ae
}))
PY
)
    # NodeBB keeps its package.json in install/ until the first setup copies it; do that copy first or npm has nothing to install.
    runuser -u nodebb -- bash -c "cd $NODEBB_DIR && cp -n install/package.json package.json && npm install --omit=dev --no-audit --no-fund && ./nodebb setup '$SETUP_JSON'"
else
    echo "== nodebb upgrade (data kept)"
    runuser -u nodebb -- bash -c "cd $NODEBB_DIR && cp install/package.json package.json && npm install --omit=dev --no-audit --no-fund && ./nodebb upgrade"
fi

echo "== systemd service ww-forums"
cat > /etc/systemd/system/ww-forums.service <<EOF
[Unit]
Description=Warriors and Wizards forums (NodeBB)
After=network.target postgresql.service

[Service]
Type=simple
User=nodebb
WorkingDirectory=$NODEBB_DIR
Environment=NODE_ENV=production
ExecStart=/usr/bin/env node loader.js --no-silent --no-daemon
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable ww-forums
systemctl restart ww-forums

echo "== nginx"
cat > /etc/nginx/conf.d/forums.conf <<EOF
map \$http_upgrade \$ww_forums_upgrade { default upgrade; '' close; }

server {
    listen 80;
    listen [::]:80;
    server_name $DOMAIN;
    client_max_body_size 10m;

    location / {
        proxy_pass http://127.0.0.1:4567;
        proxy_http_version 1.1;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection \$ww_forums_upgrade;
        proxy_redirect off;
        proxy_read_timeout 300s;
    }
}
EOF
nginx -t
systemctl enable --now nginx
systemctl reload nginx

echo "== firewall + certificate"
ufw allow 80/tcp  || true
ufw allow 443/tcp || true
certbot --nginx -d "$DOMAIN" --non-interactive --agree-tos --register-unsafely-without-email --redirect || echo "certbot failed: check the DNS A record for $DOMAIN, then re-run this script"

echo
echo "Done. Forums: https://$DOMAIN   (admin user: $ADMIN_USER - change the password in the forum's admin panel if it was typed on a command line)"
echo "Service: systemctl status ww-forums | journalctl -u ww-forums -f"
