param(
    [string]$ServerHost = "93.183.80.30",
    [string]$Username = "root",
    [string]$Password = $env:RRR_SERVER_PASSWORD,
    [string]$RemoteRoot = "/opt/rrr-dedicated",
    [string]$ServiceName = "rrr-dedicated"
)

$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $false
}

if ([string]::IsNullOrWhiteSpace($Password)) {
    throw "Pass -Password or set RRR_SERVER_PASSWORD."
}

$env:RRR_BOOTSTRAP_HOST = $ServerHost
$env:RRR_BOOTSTRAP_USERNAME = $Username
$env:RRR_BOOTSTRAP_PASSWORD = $Password
$env:RRR_BOOTSTRAP_REMOTE_ROOT = $RemoteRoot
$env:RRR_BOOTSTRAP_SERVICE_NAME = $ServiceName

@'
import os
import posixpath
import shlex

import paramiko

host = os.environ["RRR_BOOTSTRAP_HOST"]
username = os.environ["RRR_BOOTSTRAP_USERNAME"]
password = os.environ["RRR_BOOTSTRAP_PASSWORD"]
remote_root = os.environ["RRR_BOOTSTRAP_REMOTE_ROOT"].rstrip("/")
service_name = os.environ["RRR_BOOTSTRAP_SERVICE_NAME"]
service_path = f"/etc/systemd/system/{service_name}.service"

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
client.connect(host, username=username, password=password, timeout=20)

remote_root_q = shlex.quote(remote_root)
service_path_q = shlex.quote(service_path)
service_name_q = shlex.quote(service_name)

script = f"""
set -eu
mkdir -p {remote_root_q}/releases {remote_root_q}/tmp
if [ ! -f {remote_root_q}/server.env ]; then
  cat > {remote_root_q}/server.env <<'ENV'
RRR_MATCH_BACKEND_URL=http://127.0.0.1:8083
RRR_PURRNET_SOLO_BOTS=1
RRR_PURRNET_AUTO_CLOSE_SOLO_SESSION=1
RRR_PURRNET_SOLO_IDLE_TIMEOUT_SEC=30
RRR_PURRNET_SOLO_IDLE_POLL_SEC=0.5
RRR_PURRNET_MATCH_ID=purrnet-live
RRR_PURRNET_MAP_ID=city_default
RRR_DEDICATED_BIND=0.0.0.0
RRR_DEDICATED_PORT=7777
RRR_DEDICATED_LOG_LEVEL=info
RRR_DEDICATED_CONTROL_TOKEN=
RRR_DEDICATED_PUBLIC_HTTP_BASE_URL=http://127.0.0.1:7777
RRR_DEDICATED_PUBLIC_WS_BASE_URL=ws://127.0.0.1:7777
ENV
  chmod 640 {remote_root_q}/server.env
fi
cat > {service_path_q} <<'UNIT'
[Unit]
Description=RRR Unity Dedicated Server
After=network.target

[Service]
Type=simple
WorkingDirectory={remote_root}/current
EnvironmentFile=-{remote_root}/server.env
ExecStart={remote_root}/current/run.sh
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
UNIT
chmod 644 {service_path_q}
systemctl daemon-reload
systemctl enable {service_name_q}
"""

stdin, stdout, stderr = client.exec_command(script)
exit_code = stdout.channel.recv_exit_status()
error = stderr.read().decode("utf-8", "ignore")
if exit_code != 0:
    raise RuntimeError(error or f"Dedicated host bootstrap failed with exit code {exit_code}")

client.close()
'@ | python -

Write-Host "Dedicated host bootstrap complete."
Write-Host "Remote root: $RemoteRoot"
Write-Host "Service: $ServiceName"
