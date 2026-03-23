param(
    [Parameter(Mandatory = $true)]
    [string]$ReleasePath,
    [string]$ReleaseId = "",
    [string]$ServerHost = "93.183.80.30",
    [string]$Username = "root",
    [string]$Password = $env:RRR_SERVER_PASSWORD,
    [string]$RemoteRoot = "/opt/rrr-dedicated",
    [int]$KeepServerReleases = 5,
    [string]$ServiceName = "rrr-dedicated",
    [string]$PublicUrl = ""
)

$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $false
}

if (-not (Test-Path $ReleasePath)) {
    throw "Release path not found: $ReleasePath"
}

$metadataPath = Join-Path $ReleasePath "release.json"
$launchScriptPath = Join-Path $ReleasePath "run.sh"
if (-not (Test-Path $metadataPath)) {
    throw "release.json not found in release path: $ReleasePath"
}
if (-not (Test-Path $launchScriptPath)) {
    throw "run.sh not found in release path: $ReleasePath"
}

if ([string]::IsNullOrWhiteSpace($Password)) {
    throw "Pass -Password or set RRR_SERVER_PASSWORD."
}

if ([string]::IsNullOrWhiteSpace($ReleaseId)) {
    $ReleaseId = Split-Path -Leaf $ReleasePath
}

$metadata = Get-Content -Path $metadataPath -Raw | ConvertFrom-Json
$primaryArtifact = [string]$metadata.primaryArtifact
$launchScript = if ($metadata.launchScript) { [string]$metadata.launchScript } else { "run.sh" }
if ([string]::IsNullOrWhiteSpace($primaryArtifact)) {
    throw "release.json does not contain primaryArtifact."
}

$archivePath = Join-Path ([System.IO.Path]::GetTempPath()) ($ReleaseId + "-" + [Guid]::NewGuid().ToString("N") + ".zip")
Write-Host "##rrr-progress|0.10|Packaging dedicated release archive"
$env:RRR_DEDICATED_RELEASE_PATH = $ReleasePath
$env:RRR_DEDICATED_ARCHIVE_OUTPUT = $archivePath
@'
import os
import zipfile

release_path = os.environ["RRR_DEDICATED_RELEASE_PATH"]
archive_path = os.environ["RRR_DEDICATED_ARCHIVE_OUTPUT"]

with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
    for root, _, files in os.walk(release_path):
        for file_name in files:
            full_path = os.path.join(root, file_name)
            relative_path = os.path.relpath(full_path, release_path).replace("\\", "/")
            zf.write(full_path, relative_path)
'@ | python -

$env:RRR_DEDICATED_HOST = $ServerHost
$env:RRR_DEDICATED_USERNAME = $Username
$env:RRR_DEDICATED_PASSWORD = $Password
$env:RRR_DEDICATED_ARCHIVE_PATH = $archivePath
$env:RRR_DEDICATED_REMOTE_ROOT = $RemoteRoot
$env:RRR_DEDICATED_RELEASE_ID = $ReleaseId
$env:RRR_DEDICATED_KEEP_SERVER_RELEASES = [Math]::Max(1, $KeepServerReleases).ToString()
$env:RRR_DEDICATED_SERVICE_NAME = $ServiceName
$env:RRR_DEDICATED_PRIMARY_ARTIFACT = $primaryArtifact
$env:RRR_DEDICATED_LAUNCH_SCRIPT = $launchScript

$publishSucceeded = $false

try {
@'
import os
import posixpath
import shlex

import paramiko

host = os.environ["RRR_DEDICATED_HOST"]
username = os.environ["RRR_DEDICATED_USERNAME"]
password = os.environ["RRR_DEDICATED_PASSWORD"]
archive_path = os.environ["RRR_DEDICATED_ARCHIVE_PATH"]
remote_root = os.environ["RRR_DEDICATED_REMOTE_ROOT"].rstrip("/")
release_id = os.environ["RRR_DEDICATED_RELEASE_ID"]
keep_count = max(1, int(os.environ["RRR_DEDICATED_KEEP_SERVER_RELEASES"]))
service_name = os.environ["RRR_DEDICATED_SERVICE_NAME"]
primary_artifact = os.environ["RRR_DEDICATED_PRIMARY_ARTIFACT"]
launch_script = os.environ["RRR_DEDICATED_LAUNCH_SCRIPT"]

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
tmp_root = posixpath.join(remote_root, "tmp")
remote_archive = posixpath.join(tmp_root, release_id + ".zip")
remote_release = posixpath.join(releases_root, release_id)
current_link = posixpath.join(remote_root, "current")

ensure_dir(releases_root)
ensure_dir(tmp_root)
print("##rrr-progress|0.35|Uploading dedicated archive to server", flush=True)
sftp.put(archive_path, remote_archive)
sftp.close()

remote_archive_q = shlex.quote(remote_archive)
remote_release_q = shlex.quote(remote_release)
current_link_q = shlex.quote(current_link)
releases_root_q = shlex.quote(releases_root)
keep_count_q = shlex.quote(str(keep_count))
primary_artifact_q = shlex.quote(posixpath.join(remote_release, primary_artifact))
launch_script_q = shlex.quote(posixpath.join(remote_release, launch_script))
service_name_q = shlex.quote(service_name)

script = f"""
set -eu
mkdir -p {releases_root_q} {shlex.quote(tmp_root)}
rm -rf {remote_release_q}
mkdir -p {remote_release_q}
echo '##rrr-progress|0.70|Extracting dedicated release on server'
python3 - <<'PY'
import zipfile
archive = {remote_archive_q!r}
target = {remote_release_q!r}
with zipfile.ZipFile(archive, 'r') as zf:
    zf.extractall(target)
PY
rm -f {remote_archive_q}
find {remote_release_q} -type d -exec chmod 755 {{}} \\;
find {remote_release_q} -type f -exec chmod 644 {{}} \\;
chmod 755 {primary_artifact_q} {launch_script_q}
echo '##rrr-progress|0.88|Activating dedicated release'
ln -sfn {remote_release_q} {current_link_q}
systemctl restart {service_name_q}
systemctl is-active --quiet {service_name_q}
python3 - <<'PY'
import os
import shutil
releases_root = {releases_root_q!r}
keep_count = int({keep_count_q!r})
entries = []
for name in os.listdir(releases_root):
    full = os.path.join(releases_root, name)
    if os.path.isdir(full):
        entries.append((os.path.getmtime(full), full))
entries.sort(reverse=True)
for _, path in entries[keep_count:]:
    shutil.rmtree(path, ignore_errors=True)
PY
"""

stdin, stdout, stderr = client.exec_command(script)
exit_code = stdout.channel.recv_exit_status()
error = stderr.read().decode("utf-8", "ignore")
if exit_code != 0:
    raise RuntimeError(error or f"Remote dedicated release script failed with exit code {exit_code}")

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
    throw "Dedicated Server publish failed."
}

Write-Host "##rrr-progress|1.00|Dedicated Server release deployed"
Write-Host "Dedicated Server release deployed: $ReleaseId"
Write-Host "Local release: $ReleasePath"
Write-Host "Remote root: $RemoteRoot"
