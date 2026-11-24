#!/usr/bin/env bash
# Hosts Legacy.Web on Linux with Mono (FastCGI) behind nginx, on http://localhost:5080.
# This is a TEST HARNESS for environments without Windows/IIS (CI, containers). It is NOT how the legacy app
# is meant to be deployed. Prereqs: mono-complete, mono-fastcgi-server4, nginx, and a SQL Server on :1433
# whose password matches Legacy.Web/Web.config.   Usage: tools/linux/host-legacy.sh [stop]
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SITE="$ROOT/src/Legacy.Web"
CONF=/tmp/confreg-nginx.conf

stop() { pkill -f fastcgi-mono-server4 2>/dev/null || true; pkill -f "nginx -c $CONF" 2>/dev/null || true; }
[ "${1:-}" = "stop" ] && { stop; echo stopped; exit 0; }
stop

# secrets.config is git-ignored; create it from the example on first use so the demo works out of the box.
for d in Legacy.Web Legacy.Service; do
  [ -f "$ROOT/src/$d/secrets.config" ] || cp "$ROOT/config/secrets.config.example" "$ROOT/src/$d/secrets.config"
done

dotnet build "$ROOT/src/Legacy.Web" -v q
dotnet build "$ROOT/src/Legacy.Service" -v q

cat > "$CONF" <<NGINX
daemon off;
pid /tmp/confreg-nginx.pid;
error_log /tmp/confreg-nginx-error.log;
events { worker_connections 64; }
http {
  access_log off;
  client_body_temp_path /tmp/ngx-body; proxy_temp_path /tmp/ngx-proxy; fastcgi_temp_path /tmp/ngx-fcgi;
  uwsgi_temp_path /tmp/ngx-uwsgi; scgi_temp_path /tmp/ngx-scgi;
  server {
    listen 5080;
    location / {
      fastcgi_pass 127.0.0.1:9000;
      include /etc/nginx/fastcgi_params;
      fastcgi_param PATH_INFO "";
      fastcgi_param SCRIPT_FILENAME \$document_root\$fastcgi_script_name;
    }
  }
}
NGINX

nohup fastcgi-mono-server4 /applications=/:"$SITE" /socket=tcp:127.0.0.1:9000 /logfile=/tmp/confreg-fcgi.log > /tmp/confreg-fcgi.out 2>&1 &
nohup nginx -c "$CONF" > /tmp/confreg-nginx.out 2>&1 &

for i in $(seq 1 30); do
  if curl -fs "http://localhost:5080/Home/Index?format=json" >/dev/null; then echo "Legacy.Web is up on http://localhost:5080"; exit 0; fi
  sleep 1
done
echo "Legacy.Web did not start; see /tmp/confreg-fcgi.out and /tmp/confreg-nginx-error.log" >&2; exit 1
