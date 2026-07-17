<#
.SYNOPSIS
    Deploys the chat application to Azure Container Apps.

.DESCRIPTION
    Two-pass deployment:
      1. Provisions all infrastructure (ACR, PostgreSQL, Log Analytics, storage,
         Container Apps environment, and the two apps with a public placeholder image).
      2. Builds the server and web images in ACR (via 'az acr build', so no local
         Docker is required), then redeploys the apps pointing at those images.

    Run from the repository root:
        ./infra/deploy.ps1 -ResourceGroup chatapp-rg -Location northeurope

.NOTES
    Requires the Azure CLI (az) and an authenticated session (az login).
    The PostgreSQL admin password is prompted securely and passed to the
    deployment; it is not written to disk.
#>
param(
    [string]$ResourceGroup = 'chatapp-rg',
    [string]$Location = 'northeurope',
    [string]$BaseName = 'chatapp',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

# Resolve the repository root (parent of this script's folder) so paths work from anywhere.
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    # ---- Prompt for the database password (kept in memory only) ----
    $securePwd = Read-Host 'Enter PostgreSQL admin password' -AsSecureString
    $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePwd)
    try {
        $plainPwd = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }

    Write-Host "==> Ensuring resource group '$ResourceGroup' in '$Location'..." -ForegroundColor Cyan
    az group create -n $ResourceGroup -l $Location | Out-Null

    if (-not $SkipBuild) {
        # ---- Pass 1: provision infrastructure with placeholder images ----
        Write-Host '==> Pass 1: provisioning infrastructure...' -ForegroundColor Cyan
        az deployment group create `
            -g $ResourceGroup `
            -n main `
            --template-file infra/main.bicep `
            --parameters infra/main.parameters.json `
            --parameters baseName=$BaseName postgresAdminPassword=$plainPwd `
            | Out-Null

        $acrName = az deployment group show -g $ResourceGroup -n main --query properties.outputs.acrName.value -o tsv

        # ---- Build images in ACR (no local Docker needed) ----
        Write-Host '==> Building server image in ACR...' -ForegroundColor Cyan
        az acr build -r $acrName -t chat-server:latest -f docker/Dockerfile.server . | Out-Null

        Write-Host '==> Building web image in ACR...' -ForegroundColor Cyan
        az acr build -r $acrName -t chat-web:latest -f docker/Dockerfile.web . | Out-Null
    }
    else {
        Write-Host '==> Skipping image build (using existing images in ACR).' -ForegroundColor Yellow
        $acrName = az acr list -g $ResourceGroup --query "[0].name" -o tsv
    }

    $loginServer = az acr show -n $acrName --query loginServer -o tsv
    Write-Host "    ACR: $loginServer" -ForegroundColor DarkGray

    # ---- Deploy the apps with the real images ----
    Write-Host '==> Deploying app with built images...' -ForegroundColor Cyan
    az deployment group create `
        -g $ResourceGroup `
        -n main `
        --template-file infra/main.bicep `
        --parameters infra/main.parameters.json `
        --parameters baseName=$BaseName postgresAdminPassword=$plainPwd `
            serverImage="$loginServer/chat-server:latest" `
            webImage="$loginServer/chat-web:latest" `
        | Out-Null

    $webUrl = az deployment group show -g $ResourceGroup -n main --query properties.outputs.webUrl.value -o tsv
    Write-Host ''
    Write-Host '==> Deployment complete.' -ForegroundColor Green
    Write-Host "    Web UI: $webUrl" -ForegroundColor Green
}
finally {
    Pop-Location
}
