param(
    [string]$UnityEditor = "C:\Program Files\Unity\Hub\Editor\6000.3.3f1\Editor\Unity.exe",
    [string]$ProjectPath = "C:\Work\Prototyping\Russian Road Rage",
    [string]$BuildPath = "C:\Work\Prototyping\Russian Road Rage\Builds\WebGL\Latest",
    [string]$Compression = "Disabled",
    [string]$Host = "93.183.80.30",
    [string]$Username = "root",
    [string]$Password,
    [string]$RemotePath = "/var/www/rrr-webgl/play"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Password)) {
    throw "Pass -Password explicitly. It is intentionally not stored in the script."
}

if (-not (Test-Path $UnityEditor)) {
    throw "Unity editor not found: $UnityEditor"
}

$compressionArg = switch ($Compression.ToLowerInvariant()) {
    "gzip" { "gzip" }
    "brotli" { "brotli" }
    "br" { "brotli" }
    default { "disabled" }
}

New-Item -ItemType Directory -Force -Path $BuildPath | Out-Null

& $UnityEditor `
    -batchmode `
    -nographics `
    -quit `
    -projectPath $ProjectPath `
    -executeMethod WebGlBuildPipeline.BuildFromCommandLine `
    -rrrBuildPath $BuildPath `
    -rrrWebGlCompression $compressionArg `
    -logFile -

if (-not (Test-Path (Join-Path $BuildPath "index.html"))) {
    throw "WebGL build did not produce index.html at $BuildPath"
}

$env:RRR_WEBGL_HOST = $Host
$env:RRR_WEBGL_USERNAME = $Username
$env:RRR_WEBGL_PASSWORD = $Password
$env:RRR_WEBGL_LOCAL_PATH = $BuildPath
$env:RRR_WEBGL_REMOTE_PATH = $RemotePath

@'
import os
import posixpath
import tempfile

import paramiko

host = os.environ["RRR_WEBGL_HOST"]
username = os.environ["RRR_WEBGL_USERNAME"]
password = os.environ["RRR_WEBGL_PASSWORD"]
local_path = os.environ["RRR_WEBGL_LOCAL_PATH"]
remote_path = os.environ["RRR_WEBGL_REMOTE_PATH"]

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
client.connect(host, username=username, password=password, timeout=20)
sftp = client.open_sftp()

release_name = "release_" + next(tempfile._get_candidate_names())
remote_release_root = posixpath.join(posixpath.dirname(remote_path.rstrip("/")), release_name)

def ensure_dir(path):
    parts = []
    current = path
    while current not in ("", "/"):
        parts.append(current)
        current = posixpath.dirname(current)
    for part in reversed(parts):
        try:
            sftp.stat(part)
        except FileNotFoundError:
            sftp.mkdir(part)

def upload_dir(local_dir, remote_dir):
    ensure_dir(remote_dir)
    for entry in os.listdir(local_dir):
        local_entry = os.path.join(local_dir, entry)
        remote_entry = posixpath.join(remote_dir, entry)
        if os.path.isdir(local_entry):
            upload_dir(local_entry, remote_entry)
        else:
            sftp.put(local_entry, remote_entry)

ensure_dir(posixpath.dirname(remote_path.rstrip("/")))
upload_dir(local_path, remote_release_root)

stdin, stdout, stderr = client.exec_command(
    f"mkdir -p {posixpath.dirname(remote_path.rstrip('/'))} && "
    f"rm -rf {remote_path}.bak && "
    f"if [ -e {remote_path} ]; then mv {remote_path} {remote_path}.bak; fi && "
    f"mv {remote_release_root} {remote_path} && "
    f"find {remote_path} -type d -exec chmod 755 {{}} \\; && "
    f"find {remote_path} -type f -exec chmod 644 {{}} \\;"
)
stdout.channel.recv_exit_status()
error = stderr.read().decode("utf-8", "ignore")
if error.strip():
    raise RuntimeError(error)

sftp.close()
client.close()
'@ | python -

Write-Host "WebGL build uploaded to https://rrr-demo.tonforspeed.space/play/"
