#!/bin/bash
# One-time (and re-runnable) setup of The Portal on the VPS (Ubuntu / Debian). Run as root:
#     bash setup_portal.sh portal.warriorsandwizards.com 104.152.50.196
# Prerequisite: a DNS "A" record  portal  ->  the VPS (DNS-only / grey cloud in Cloudflare) so Let's Encrypt can verify the name.
# What it sets up:
#   * nginx serving https://<domain> from /var/www/portal (the static site that deploy -Portal uploads),
#   * /api/ on that name proxied to the account server (only its /public/... answers are useful there; everything else on the
#     account server still needs the game's own login), with pretty URLs (/player/Name, /guild/Name, /top/fame, /wiki/...),
#   * a Let's Encrypt certificate, auto-renewed.
# Nothing here touches the game servers, the database or the other sites on this box.
set -e
DOMAIN="${1:?usage: bash setup_portal.sh <domain> <account server ip>}"
IP="${2:?account server ip missing}"
ROOT=/var/www/portal

echo "== packages"
apt-get update
apt-get install -y nginx certbot python3-certbot-nginx
mkdir -p "$ROOT"
[ -f "$ROOT/index.html" ] || echo '<!doctype html><title>The Portal</title><p>The Portal is being set up. Upload it with deploy -Portal.' > "$ROOT/index.html"

echo "== nginx site"
cat > /etc/nginx/sites-available/portal <<EOF
server {
    listen 80;
    server_name $DOMAIN;
    root $ROOT;
    index index.html;
    gzip on;
    gzip_types text/css application/javascript application/json image/svg+xml;

    # the site itself: short cache for the pages, long for icons (their content never changes without a new file name... they may,
    # so keep it to a day)
    location = /index.html { add_header Cache-Control "no-cache"; }
    location /icons/ { add_header Cache-Control "public, max-age=86400"; }
    location /data/  { add_header Cache-Control "public, max-age=300"; }

    # pretty URLs, RealmEye style: the page reads the last path segment
    location /player/ { try_files \$uri /player.html; }
    location /guild/  { try_files \$uri /guild.html; }
    location /top/    { try_files \$uri /leaderboards.html; }
    location = /wiki/items   { try_files /items.html =404; }
    location = /wiki/classes { try_files /classes.html =404; }
    location /wiki/item/  { try_files \$uri /items.html; }
    location /wiki/class/ { try_files \$uri /classes.html; }
    location = /graveyard { try_files /graveyard.html =404; }
    location = /wiki { return 302 /wiki/items; }

    # the public API: only the read-only /public/ endpoints of the account server are reachable through this name
    location /api/public/ {
        proxy_pass http://$IP:8080/public/;
        proxy_set_header Host $IP:8080;   # the account server only answers requests addressed to its own listen address
        proxy_set_header X-Forwarded-For \$remote_addr;
        proxy_read_timeout 30s;
        add_header Cache-Control "public, max-age=30";
    }
    location /api/ { return 404; }
}
EOF
ln -sf /etc/nginx/sites-available/portal /etc/nginx/sites-enabled/portal
nginx -t
systemctl enable --now nginx
systemctl reload nginx

echo "== firewall + certificate"
ufw allow 80/tcp  || true
ufw allow 443/tcp || true
certbot --nginx -d "$DOMAIN" --non-interactive --agree-tos --register-unsafely-without-email --redirect

echo
echo "Done. Upload the site with  .\\deploy.ps1 -Portal  (from the repo folder on your PC), then open https://$DOMAIN"
