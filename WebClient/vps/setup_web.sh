#!/bin/bash
# One-time (and re-runnable) setup of the browser client on the VPS (Ubuntu). Run as root:
#     bash setup_web.sh play.warriorsandwizards.com
# Prerequisite: a DNS "A" record  play  ->  104.152.50.196  (DNS-only / grey cloud in Cloudflare) so Let's Encrypt can verify the name.
# What it sets up:
#   * nginx serving the static web client from /var/www/warriors over HTTPS (Let's Encrypt certificate, auto-renewed),
#   * /api/  -> the account server (plain HTTP on the VPS's own IP :8080),
#   * /game  -> a small WebSocket -> TCP bridge (ws_bridge.py, systemd service "ww-bridge") to the game server :2050,
# so the browser only ever talks HTTPS/WSS to one origin, and the game server itself is unchanged.
set -e
DOMAIN="${1:?usage: bash setup_web.sh <domain>   e.g. play.warriorsandwizards.com}"
IP="104.152.50.196"
HERE="$(cd "$(dirname "$0")" && pwd)"

apt-get update
apt-get install -y nginx certbot python3-certbot-nginx python3-venv
mkdir -p /var/www/warriors /opt/ww-bridge

# ---- WebSocket -> TCP bridge -------------------------------------------------------------------------------------------
python3 -m venv /opt/ww-bridge/venv
/opt/ww-bridge/venv/bin/pip install --quiet websockets
cp "$HERE/ws_bridge.py" /opt/ww-bridge/ws_bridge.py
cat > /etc/systemd/system/ww-bridge.service <<EOF
[Unit]
Description=Warriors and Wizards WebSocket bridge
After=network.target alloy-game.service

[Service]
ExecStart=/opt/ww-bridge/venv/bin/python /opt/ww-bridge/ws_bridge.py 2051 127.0.0.1 2050
Restart=always
RestartSec=3

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable --now ww-bridge

# ---- nginx --------------------------------------------------------------------------------------------------------------
cat > /etc/nginx/conf.d/warriors.conf <<EOF
types { application/wasm wasm; }
map \$http_upgrade \$ww_connection_upgrade { default upgrade; '' close; }

server {
    listen 80;
    listen [::]:80;
    server_name $DOMAIN;
    root /var/www/warriors;

    gzip_static on;                         # the build ships pre-compressed .gz files next to the originals
    gzip on;
    gzip_types text/plain text/css application/javascript application/json application/wasm application/octet-stream;

    location / {
        try_files \$uri \$uri/ =404;
        add_header Cache-Control "no-cache";
    }
    # Only FINGERPRINTED runtime files (name.<10 chars>.ext) never change under the same name. dotnet.js is not fingerprinted and lists the others by
    # name, so it must always be revalidated - an immutable copy of it left browsers asking for files a redeploy had deleted (the page hung on load
    # until Ctrl+F5).
    location /_framework/ {
        add_header Cache-Control "no-cache";
    }
    location ~ "^/_framework/.+\\.[a-z0-9]{10}\\.(js|wasm|dat|json|dll|pdb)\$" {
        add_header Cache-Control "public, max-age=31536000, immutable";
    }
    location /api/ {
        proxy_pass http://$IP:8080/;
        proxy_set_header Host \$host;
        proxy_set_header X-Forwarded-For \$remote_addr;
        client_max_body_size 1m;
        proxy_read_timeout 60s;
    }
    location /game {
        proxy_pass http://127.0.0.1:2051;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection \$ww_connection_upgrade;
        proxy_read_timeout 3600s;
        proxy_send_timeout 3600s;
    }
}
EOF
rm -f /etc/nginx/sites-enabled/default
nginx -t
systemctl enable --now nginx
systemctl reload nginx

# ---- firewall + certificate -------------------------------------------------------------------------------------------
ufw allow 80/tcp  || true
ufw allow 443/tcp || true
certbot --nginx -d "$DOMAIN" --non-interactive --agree-tos --register-unsafely-without-email --redirect

echo
echo "Done. Upload the site with  .\\deploy.ps1 -Web  (from the repo folder on your PC), then open https://$DOMAIN"
