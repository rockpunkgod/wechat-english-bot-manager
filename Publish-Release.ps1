# Run in the Windows account already signed in to GitHub through Git.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot.Replace('\','/')
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'package.json') -Raw | ConvertFrom-Json).version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version.' }
$tag = 'v' + $version
function Git {
    & git.exe -C $root -c ("safe.directory=" + $root) -c http.sslBackend=openssl @args
    if ($LASTEXITCODE -ne 0) { throw 'Git command failed; nothing will be force-pushed.' }
}
if ((Git branch --show-current) -ne 'main') { throw 'Publish from main.' }
if (@(Git status --porcelain).Count -gt 0) { throw 'Commit all release source changes before publishing.' }
$head = Git rev-parse HEAD
$existing = @(Git tag --list $tag)
if ($existing.Count -eq 0) { Git tag -a $tag -m ("Release " + $tag) }
elseif ((Git rev-list -n 1 $tag) -ne $head) { throw 'Existing version tag points to another commit; choose a new version.' }
Git push --atomic --set-upstream origin main ("refs/tags/" + $tag)
Write-Host ("Pushed " + $tag + ". GitHub Actions will build, test and publish the setup.")
Write-Host 'https://github.com/rockpunkgod/wechat-english-bot-manager/actions'
