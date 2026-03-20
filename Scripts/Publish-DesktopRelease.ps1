param(
    [Parameter(Mandatory = $true)]
    [string]$ReleasePath,
    [string]$ReleaseId = "",
    [string]$ServerHost = "93.183.80.30",
    [string]$Username = "root",
    [string]$Password = $env:RRR_SERVER_PASSWORD,
    [string]$RemoteRoot = "/var/www/rrr-downloads/windows",
    [int]$KeepServerReleases = 5,
    [string]$PublicUrl = "https://rrr-demo.tonforspeed.space/downloads/windows/latest.zip"
)

$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $false
}

if (-not (Test-Path $ReleasePath)) {
    throw "Release path not found: $ReleasePath"
}

$metadataPath = Join-Path $ReleasePath "release.json"
if (-not (Test-Path $metadataPath)) {
    throw "release.json not found in release path: $ReleasePath"
}

$exeFiles = Get-ChildItem -Path $ReleasePath -Filter *.exe -File
if ($exeFiles.Count -eq 0) {
    throw "No .exe file found in release path: $ReleasePath"
}

if ([string]::IsNullOrWhiteSpace($Password)) {
    throw "Pass -Password or set RRR_SERVER_PASSWORD."
}

if ([string]::IsNullOrWhiteSpace($ReleaseId)) {
    $ReleaseId = Split-Path -Leaf $ReleasePath
}

$archivePath = Join-Path ([System.IO.Path]::GetTempPath()) ($ReleaseId + "-" + [Guid]::NewGuid().ToString("N") + ".zip")
Write-Host "##rrr-progress|0.10|Packaging desktop release archive"
$env:RRR_DESKTOP_RELEASE_PATH = $ReleasePath
$env:RRR_DESKTOP_ARCHIVE_OUTPUT = $archivePath
@'
import os
import zipfile

release_path = os.environ["RRR_DESKTOP_RELEASE_PATH"]
archive_path = os.environ["RRR_DESKTOP_ARCHIVE_OUTPUT"]

with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
    for root, _, files in os.walk(release_path):
        for file_name in files:
            full_path = os.path.join(root, file_name)
            relative_path = os.path.relpath(full_path, release_path).replace("\\", "/")
            zf.write(full_path, relative_path)
'@ | python -

$env:RRR_DESKTOP_HOST = $ServerHost
$env:RRR_DESKTOP_USERNAME = $Username
$env:RRR_DESKTOP_PASSWORD = $Password
$env:RRR_DESKTOP_ARCHIVE_PATH = $archivePath
$env:RRR_DESKTOP_METADATA_PATH = $metadataPath
$env:RRR_DESKTOP_REMOTE_ROOT = $RemoteRoot
$env:RRR_DESKTOP_RELEASE_ID = $ReleaseId
$env:RRR_DESKTOP_KEEP_SERVER_RELEASES = [Math]::Max(1, $KeepServerReleases).ToString()

$publishSucceeded = $false

try {
@'
import os
import posixpath
import shlex

import paramiko

host = os.environ["RRR_DESKTOP_HOST"]
username = os.environ["RRR_DESKTOP_USERNAME"]
password = os.environ["RRR_DESKTOP_PASSWORD"]
archive_path = os.environ["RRR_DESKTOP_ARCHIVE_PATH"]
metadata_path = os.environ["RRR_DESKTOP_METADATA_PATH"]
remote_root = os.environ["RRR_DESKTOP_REMOTE_ROOT"].rstrip("/")
release_id = os.environ["RRR_DESKTOP_RELEASE_ID"]
keep_count = max(1, int(os.environ["RRR_DESKTOP_KEEP_SERVER_RELEASES"]))

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
client.connect(host, username=username, password=password, timeout=20)
sftp = client.open_sftp()

def ensure_dir(path: str) -> None:
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

releases_root = posixpath.join(remote_root, "releases")
ensure_dir(remote_root)
ensure_dir(releases_root)

remote_zip = posixpath.join(releases_root, release_id + ".zip")
remote_json = posixpath.join(releases_root, release_id + ".json")
latest_zip = posixpath.join(remote_root, "latest.zip")
latest_json = posixpath.join(remote_root, "latest.json")

print("##rrr-progress|0.35|Uploading desktop archive", flush=True)
sftp.put(archive_path, remote_zip)
print("##rrr-progress|0.55|Uploading desktop metadata", flush=True)
sftp.put(metadata_path, remote_json)
sftp.close()

remote_zip_q = shlex.quote(remote_zip)
remote_json_q = shlex.quote(remote_json)
latest_zip_q = shlex.quote(latest_zip)
latest_json_q = shlex.quote(latest_json)
releases_root_q = shlex.quote(releases_root)
keep_count_q = shlex.quote(str(keep_count))
remote_root_q = shlex.quote(remote_root)

script = f"""
set -eu
echo '##rrr-progress|0.80|Updating latest download links'
ln -sfn {remote_zip_q} {latest_zip_q}
ln -sfn {remote_json_q} {latest_json_q}
cat > {remote_root_q}/index.html <<'HTML'
<!doctype html>
<html lang=\"en\">
<head>
  <meta charset=\"utf-8\" />
  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />
  <title>Russian Road Rage Downloads</title>
  <style>
    body {{ font: 16px/1.5 Segoe UI, sans-serif; margin: 40px; background: #101418; color: #f5f6f8; }}
    a {{ color: #7dd3fc; }}
    code {{ color: #fbbf24; }}
  </style>
</head>
<body>
  <h1>Russian Road Rage Downloads</h1>
  <p>Latest Windows build: <a href=\"/downloads/windows/latest.zip\">latest.zip</a></p>
  <p>Metadata: <a href=\"/downloads/windows/latest.json\">latest.json</a></p>
</body>
</html>
HTML
chmod 644 {remote_root_q}/index.html
find {releases_root_q} -maxdepth 1 -type f -name '*.zip' -printf '%T@ %p\\n' | sort -nr | awk 'NR>{keep_count_q} {{print $2}}' | while read -r old_zip; do rm -f \"$old_zip\" \"${{old_zip%.zip}}.json\"; done
"""

stdin, stdout, stderr = client.exec_command(script)
exit_code = stdout.channel.recv_exit_status()
error = stderr.read().decode("utf-8", "ignore")
if exit_code != 0:
    raise RuntimeError(error or f"Remote desktop release script failed with exit code {exit_code}")

client.close()
'@ | python -
    $publishSucceeded = $true
}
finally {
    if (Test-Path $archivePath) {
        Remove-Item -Force $archivePath -ErrorAction SilentlyContinue
    }
}

if (-not $publishSucceeded) {
    throw "Desktop publish failed."
}

Write-Host "##rrr-progress|1.00|Desktop release deployed"
Write-Host "Desktop release deployed: $ReleaseId"
Write-Host "Local release: $ReleasePath"
Write-Host "Public URL: $PublicUrl"
