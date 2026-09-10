$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$toolRoot = Join-Path $repoRoot 'build\tools'
New-Item -ItemType Directory -Path $toolRoot -Force | Out-Null
$installer = Join-Path $toolRoot 'innosetup-6.7.3.exe'
$expectedHash = '9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732'
if (-not (Test-Path -LiteralPath $installer)) {
    Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -OutFile $installer
}
if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Inno Setup download checksum mismatch.' }
$signature = Get-AuthenticodeSignature -LiteralPath $installer
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Pyrsys B\.V\.') {
    throw 'Inno Setup publisher signature verification failed.'
}
$destination = Join-Path $toolRoot 'inno-6.7.3'
$compiler = Join-Path $destination 'ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $process = Start-Process -FilePath $installer -ArgumentList @('/CURRENTUSER','/PORTABLE=1','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/NOICONS',('/DIR="' + $destination + '"')) -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) { throw 'Compiler preparation timed out.' }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $compiler)) { throw 'Compiler preparation failed.' }
}
$compiler
