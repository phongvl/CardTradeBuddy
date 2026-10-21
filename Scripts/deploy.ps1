<#
.SYNOPSIS
    Sets up IIS for CardTradeBuddy (app pool, site, bindings, permissions) and deploys the app.

.DESCRIPTION
    1. Creates or updates the app pool ("No Managed Code") and the site, bound to cardtradebuddy.com and
       www.cardtradebuddy.com on HTTP port 80
    2. Gives the app pool identity read access to the site folder and modify access to logs\
    3. dotnet publish (Release) into a temporary folder
    4. Drops app_offline.htm in the target so IIS stops the app and releases locked files
    5. Mirrors the published output into the target (logs\ is left alone)
    6. Removes app_offline.htm and makes sure the pool and site are started

    Safe to run repeatedly: existing pool, site and bindings are updated, not duplicated.

    Run from an elevated PowerShell on the server. The IIS role and the ASP.NET Core Hosting Bundle for
    .NET 10 must be installed.

    HTTPS: one https binding per host name on port 443 (SNI, so other sites can share the port). The certificate
    comes from, in order: -CertThumbprint, a certificate already in LocalMachine\My or \WebHosting (win-acme's store) that is valid for every host
    name (a wildcard such as *.cardtradebuddy.com counts), or a new self-signed one if -CreateSelfSignedCert is
    given. If none is found, https is skipped with a warning and http still works. Get a real certificate (for
    example with win-acme / Let's Encrypt) and run the script again with its thumbprint.

    By default the pool runs as the built-in ApplicationPoolIdentity (IIS AppPool\CardTradeBuddy), which needs
    no password. To run it as a specific account instead, pass -PoolCredential (Get-Credential); the password
    is only kept in memory for this run and is stored by IIS, never written to this script.

.EXAMPLE
    .\Scripts\deploy.ps1
    .\Scripts\deploy.ps1 -PoolCredential (Get-Credential)
    .\Scripts\deploy.ps1 -SkipIisSetup          # only publish and copy
    .\Scripts\deploy.ps1 -CertThumbprint 0123ABCD...   # use this certificate for https
    .\Scripts\deploy.ps1 -CreateSelfSignedCert         # testing only: browsers will warn
    .\Scripts\deploy.ps1 -SkipHttps                    # http bindings only
#>
[CmdletBinding()]
param(
    [string]$Target = 'C:\inetpub\wwwroot\CardTradeBuddy',
    [string]$Configuration = 'Release',
    [string]$SiteName = 'CardTradeBuddy',
    [string]$PoolName = 'CardTradeBuddy',
    [string[]]$HostNames = @('cardtradebuddy.com', 'www.cardtradebuddy.com'),
    [int]$Port = 80,
    [int]$HttpsPort = 443,
    [string]$CertThumbprint,
    [switch]$CreateSelfSignedCert,
    [switch]$SkipHttps,
    [pscredential]$PoolCredential,
    [switch]$SkipIisSetup
)

$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated (Administrator) PowerShell.'
}

# This script lives in <project>\Scripts, the project file is one folder up.
$project = Join-Path (Split-Path $PSScriptRoot -Parent) 'CardTradeBuddy.csproj'
if (-not (Test-Path $project)) {
    throw "Project file not found: $project"
}

# The account that runs the app: a custom one if given, otherwise the pool's virtual account.
$poolAccount = if ($PoolCredential) { $PoolCredential.UserName } else { "IIS AppPool\$PoolName" }

function Test-CertCoversHost($cert, [string]$hostName) {
    foreach ($name in $cert.DnsNameList.Unicode) {
        if ($name -ieq $hostName) { return $true }
        # wildcard: *.example.com covers www.example.com but not example.com itself
        if ($name.StartsWith('*.') -and $hostName.EndsWith($name.Substring(1), 'OrdinalIgnoreCase') `
                -and $hostName.IndexOf('.') -eq $hostName.Length - $name.Length + 1) { return $true }
    }
    return $false
}

# "My" is where certificates are normally imported; win-acme (Let's Encrypt) puts its certificates in "WebHosting".
$CertStores = @('My', 'WebHosting')

function Get-StoreName($cert) { Split-Path $cert.PSParentPath -Leaf }

function Resolve-Certificate {
    if ($CertThumbprint) {
        $thumb = $CertThumbprint -replace '\s', ''
        $cert = $CertStores | ForEach-Object { Get-Item "Cert:\LocalMachine\$_\$thumb" -ErrorAction SilentlyContinue } | Select-Object -First 1
        if (-not $cert) { throw "No certificate with thumbprint $thumb in LocalMachine\My or LocalMachine\WebHosting." }
        if (-not $cert.HasPrivateKey) { throw "Certificate $thumb has no private key; IIS cannot use it." }
        return $cert
    }

    $found = $CertStores | ForEach-Object { Get-ChildItem "Cert:\LocalMachine\$_" -ErrorAction SilentlyContinue } |
        Where-Object {
            $c = $_
            $c.HasPrivateKey -and $c.NotAfter -gt (Get-Date) -and
            -not ($HostNames | Where-Object { -not (Test-CertCoversHost $c $_) })
        } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
    if ($found) { return $found }

    if ($CreateSelfSignedCert) {
        Write-Host 'Creating a self-signed certificate (testing only; browsers will show a warning) ...' -ForegroundColor Yellow
        return New-SelfSignedCertificate -DnsName $HostNames -CertStoreLocation Cert:\LocalMachine\My `
            -FriendlyName 'CardTradeBuddy (self-signed)' -NotAfter (Get-Date).AddYears(1)
    }

    return $null
}

function Initialize-Https {
    $cert = Resolve-Certificate
    if (-not $cert) {
        Write-Warning ("No certificate covering {0} was found, so https is NOT set up. " -f ($HostNames -join ', ') +
            'Install one and rerun with -CertThumbprint <thumbprint>, or use -CreateSelfSignedCert for testing.')
        return $false
    }
    Write-Host "Using certificate $($cert.Thumbprint) ($($cert.Subject), expires $($cert.NotAfter.ToString('yyyy-MM-dd')))" -ForegroundColor Cyan

    foreach ($hostName in $HostNames) {
        $binding = Get-WebBinding -Name $SiteName -Protocol https -Port $HttpsPort -HostHeader $hostName
        if (-not $binding) {
            Write-Host "Adding binding https://${hostName}:$HttpsPort ..." -ForegroundColor Cyan
            # SslFlags 1 = Server Name Indication, so the host header picks the certificate
            New-WebBinding -Name $SiteName -Protocol https -Port $HttpsPort -HostHeader $hostName -IPAddress '*' -SslFlags 1
            $binding = Get-WebBinding -Name $SiteName -Protocol https -Port $HttpsPort -HostHeader $hostName
        }

        # Replace any certificate already attached to this host name, so reruns pick up a renewed one.
        $sslPath = "IIS:\SslBindings\!$HttpsPort!$hostName"
        if (Test-Path $sslPath) { Remove-Item $sslPath -Force }
        $binding.AddSslCertificate($cert.Thumbprint, (Get-StoreName $cert))
    }
    return $true
}

function Initialize-Iis {
    Import-Module WebAdministration -ErrorAction Stop

    if (-not (Get-WebGlobalModule -Name 'AspNetCoreModuleV2' -ErrorAction SilentlyContinue)) {
        Write-Warning 'AspNetCoreModuleV2 is not installed. Install the ASP.NET Core Hosting Bundle for .NET 10, then run iisreset.'
    }

    # --- App pool: no managed runtime, because ASP.NET Core runs out of process / in its own host ---
    $poolPath = "IIS:\AppPools\$PoolName"
    if (-not (Test-Path $poolPath)) {
        Write-Host "Creating app pool '$PoolName' ..." -ForegroundColor Cyan
        New-WebAppPool -Name $PoolName | Out-Null
    }
    Set-ItemProperty $poolPath -Name managedRuntimeVersion -Value ''

    if ($PoolCredential) {
        Write-Host "App pool identity: $($PoolCredential.UserName)" -ForegroundColor Cyan
        Set-ItemProperty $poolPath -Name processModel -Value @{
            identityType = 'SpecificUser'
            userName     = $PoolCredential.UserName
            password     = $PoolCredential.GetNetworkCredential().Password
        }
    }
    else {
        Set-ItemProperty $poolPath -Name processModel.identityType -Value 'ApplicationPoolIdentity'
    }

    # --- Site folder and permissions ---
    New-Item -ItemType Directory -Path $Target -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $Target 'logs') -Force | Out-Null

    Write-Host "Granting '$poolAccount' access to $Target ..." -ForegroundColor Cyan
    icacls $Target /grant "${poolAccount}:(OI)(CI)RX" /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "icacls failed for $Target (exit code $LASTEXITCODE)." }
    icacls (Join-Path $Target 'logs') /grant "${poolAccount}:(OI)(CI)M" /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "icacls failed for the logs folder (exit code $LASTEXITCODE)." }

    # --- Site and bindings ---
    $sitePath = "IIS:\Sites\$SiteName"
    if (-not (Test-Path $sitePath)) {
        Write-Host "Creating site '$SiteName' ..." -ForegroundColor Cyan
        New-Website -Name $SiteName -PhysicalPath $Target -ApplicationPool $PoolName `
            -Port $Port -HostHeader $HostNames[0] -IPAddress '*' | Out-Null
    }
    Set-ItemProperty $sitePath -Name physicalPath -Value $Target
    Set-ItemProperty $sitePath -Name applicationPool -Value $PoolName

    foreach ($hostName in $HostNames) {
        $existing = Get-WebBinding -Name $SiteName -Protocol http -Port $Port -HostHeader $hostName
        if (-not $existing) {
            Write-Host "Adding binding http://${hostName}:$Port ..." -ForegroundColor Cyan
            New-WebBinding -Name $SiteName -Protocol http -Port $Port -HostHeader $hostName -IPAddress '*'
        }
    }

    $script:httpsEnabled = $false
    if (-not $SkipHttps) {
        $script:httpsEnabled = Initialize-Https
    }
}

$publishDir = Join-Path ([IO.Path]::GetTempPath()) "CardTradeBuddy-publish-$([guid]::NewGuid().ToString('N'))"
$offlineFile = Join-Path $Target 'app_offline.htm'

try {
    if (-not $SkipIisSetup) {
        Initialize-Iis
    }
    elseif (-not (Test-Path $Target)) {
        New-Item -ItemType Directory -Path $Target | Out-Null
    }

    Write-Host "Publishing ($Configuration) to $publishDir ..." -ForegroundColor Cyan
    dotnet publish $project -c $Configuration -o $publishDir --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    Write-Host 'Taking the app offline ...' -ForegroundColor Cyan
    Set-Content -Path $offlineFile -Encoding utf8 -Value '<html><body><h1>CardTradeBuddy is being updated. Back in a moment.</h1></body></html>'
    Start-Sleep -Seconds 3   # give the IIS worker process time to shut down and release its files

    Write-Host "Copying files to $Target ..." -ForegroundColor Cyan
    # /MIR removes files in the target that are no longer in the build. Kept: the log folder, the offline page, and
    # .well-known (Let's Encrypt validation files written by win-acme, which must survive a deploy).
    robocopy $publishDir $Target /MIR /XD logs .well-known /XF app_offline.htm /NFL /NDL /NJH /NJS /NP
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE." }

    Write-Host 'Deployed.' -ForegroundColor Green
}
finally {
    # Always bring the site back, even if something above failed.
    if (Test-Path $offlineFile) {
        Remove-Item $offlineFile -Force
        Write-Host 'App is back online.' -ForegroundColor Cyan
    }
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }
}

if (-not $SkipIisSetup) {
    Import-Module WebAdministration
    if ((Get-WebAppPoolState -Name $PoolName).Value -ne 'Started') { Start-WebAppPool -Name $PoolName }
    if ((Get-WebsiteState -Name $SiteName).Value -ne 'Started') { Start-Website -Name $SiteName }
    $urls = $HostNames | ForEach-Object { "http://${_}:$Port" }
    if ($script:httpsEnabled) { $urls += $HostNames | ForEach-Object { "https://${_}:$HttpsPort" } }
    Write-Host ("Site '{0}' is running: {1}" -f $SiteName, ($urls -join ', ')) -ForegroundColor Green
}

