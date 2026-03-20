param(
    [string]$ServerHost = "93.183.80.30",
    [string]$Username = "root",
    [string]$Password = $env:RRR_SERVER_PASSWORD,
    [string]$RemoteRoot = "/var/www/rrr-webgl",
    [string]$NginxSitePath = "/etc/nginx/sites-available/rrr-demo.tonforspeed.space"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Password)) {
    throw "Pass -Password or set RRR_SERVER_PASSWORD."
}

$env:RRR_WEBGL_HOST = $ServerHost
$env:RRR_WEBGL_USERNAME = $Username
$env:RRR_WEBGL_PASSWORD = $Password
$env:RRR_WEBGL_REMOTE_ROOT = $RemoteRoot
$env:RRR_WEBGL_NGINX_SITE_PATH = $NginxSitePath

@'
import os
import posixpath
import re
import textwrap

import paramiko

host = os.environ["RRR_WEBGL_HOST"]
username = os.environ["RRR_WEBGL_USERNAME"]
password = os.environ["RRR_WEBGL_PASSWORD"]
remote_root = os.environ["RRR_WEBGL_REMOTE_ROOT"].rstrip("/")
nginx_site_path = os.environ["RRR_WEBGL_NGINX_SITE_PATH"]
brotli_filter_conf = "/etc/nginx/modules-enabled/50-mod-http-brotli-filter.conf"
brotli_static_conf = "/etc/nginx/modules-enabled/50-mod-http-brotli-static.conf"

begin_marker = "    # BEGIN RRR WEBGL"
end_marker = "    # END RRR WEBGL"

block = textwrap.dedent(f"""
    # BEGIN RRR WEBGL
    location = /play {{
        return 301 /play/;
    }}

    location /play/ {{
        alias {remote_root}/current/;
        index index.html;
        gzip_static on;
        brotli_static on;
        add_header Vary "Accept-Encoding" always;
        try_files $uri $uri/ /index.html;
    }}

    location ~* ^/play/(?P<wasm_path>.*\\.wasm)$ {{
        alias {remote_root}/current/$wasm_path;
        gzip_static on;
        brotli_static on;
        default_type application/wasm;
        add_header Cache-Control "public, max-age=31536000, immutable";
        add_header Vary "Accept-Encoding" always;
    }}

    location ~* ^/play/(?P<asset_path>.*\\.(data|symbols\\.json|js))$ {{
        alias {remote_root}/current/$asset_path;
        gzip_static on;
        brotli_static on;
        add_header Cache-Control "public, max-age=31536000, immutable";
        add_header Vary "Accept-Encoding" always;
    }}
    # END RRR WEBGL
""").strip("\n") + "\n\n"

placeholder = textwrap.dedent("""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Russian Road Rage WebGL</title>
  <style>
    :root { color-scheme: dark; }
    body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: radial-gradient(circle at top, #1f2833, #0b0d11 60%); color: #f6f7f8; font: 16px/1.5 Segoe UI, sans-serif; }
    main { width: min(720px, calc(100vw - 48px)); padding: 32px; border: 1px solid rgba(255,255,255,.12); border-radius: 18px; background: rgba(9,12,16,.78); box-shadow: 0 24px 80px rgba(0,0,0,.35); }
    h1 { margin: 0 0 12px; font-size: 32px; }
    p { margin: 0 0 12px; color: rgba(246,247,248,.8); }
    code { color: #ffd36b; }
  </style>
</head>
<body>
  <main>
    <h1>Russian Road Rage WebGL</h1>
    <p>The WebGL host is configured and waiting for the first release.</p>
    <p>Public URL: <code>/play/</code></p>
  </main>
</body>
</html>
""").strip() + "\n"

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
client.connect(host, username=username, password=password, timeout=20)
sftp = client.open_sftp()

for module_conf, module_line in [
    (brotli_filter_conf, "load_module modules/ngx_http_brotli_filter_module.so;\n"),
    (brotli_static_conf, "load_module modules/ngx_http_brotli_static_module.so;\n"),
]:
    try:
        with sftp.open(module_conf, "r") as f:
            existing = f.read().decode("utf-8")
        if module_line.strip() in existing:
            continue
    except FileNotFoundError:
        pass

    with sftp.open(module_conf, "w") as f:
        f.write(module_line.encode("utf-8"))

with sftp.open(nginx_site_path, "r") as f:
    content = f.read().decode("utf-8")

pattern = re.compile(re.escape(begin_marker) + r".*?" + re.escape(end_marker) + r"\n*", re.S)

legacy_marker = "    location = /play {"
main_location_marker = "    location / {"
if legacy_marker in content and main_location_marker in content:
    legacy_start = content.index(legacy_marker)
    main_location_start = content.index(main_location_marker)
    if legacy_start < main_location_start:
        content = content[:legacy_start] + content[main_location_start:]

if pattern.search(content):
    content = pattern.sub(block, content)
else:
    if main_location_marker not in content:
        raise RuntimeError("Could not find insertion marker in nginx site config")
    content = content.replace(main_location_marker, block + main_location_marker, 1)

with sftp.open(nginx_site_path, "w") as f:
    f.write(content.encode("utf-8"))

for path in [
    remote_root,
    posixpath.join(remote_root, "releases"),
    posixpath.join(remote_root, "tmp"),
    posixpath.join(remote_root, "releases", "placeholder"),
]:
    stdin, stdout, stderr = client.exec_command(f"mkdir -p {path}")
    stdout.channel.recv_exit_status()

placeholder_path = posixpath.join(remote_root, "releases", "placeholder", "index.html")
with sftp.open(placeholder_path, "w") as f:
    f.write(placeholder.encode("utf-8"))

commands = [
    f"ln -sfn {posixpath.join(remote_root, 'releases', 'placeholder')} {posixpath.join(remote_root, 'current')}",
    f"chmod -R 755 {remote_root}",
    "nginx -t",
    "systemctl reload nginx",
]

for command in commands:
    stdin, stdout, stderr = client.exec_command(command)
    exit_code = stdout.channel.recv_exit_status()
    error = stderr.read().decode('utf-8', 'ignore')
    if exit_code != 0:
        raise RuntimeError(error or f"Command failed: {command}")

sftp.close()
client.close()
'@ | python -

Write-Host "WebGL host configured at https://rrr-demo.tonforspeed.space/play/"
