param(
    [Parameter(Mandatory = $true)][string]$UnityPath
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskProject = Join-Path $taskRoot '.validation/FilterSmoke'
$taskAssets = Join-Path $taskProject 'Assets/CameraCaptureStudio'
$taskSource = Join-Path $taskRoot 'Packages/com.camera-capture.studio'
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw "Unity editor not found: $UnityPath" }
New-Item -ItemType Directory -Path $taskAssets, (Join-Path $taskProject 'Packages'), (Join-Path $taskProject 'ProjectSettings') -Force | Out-Null
$taskVersionPath = Join-Path $taskProject 'ProjectSettings/ProjectVersion.txt'
if (-not (Test-Path -LiteralPath $taskVersionPath)) {
    # Make this an existing minimal project, so Unity does not insert template packages.
    [System.IO.File]::WriteAllText($taskVersionPath, "m_EditorVersion: 2022.3.0f1`n", [System.Text.UTF8Encoding]::new($false))
}
Copy-Item -LiteralPath (Join-Path $taskSource 'Editor'), (Join-Path $taskSource 'Fonts') -Destination $taskAssets -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FilterSmoke.cs') -Destination (Join-Path $taskAssets 'Editor/FilterSmoke.cs') -Force
[System.IO.File]::WriteAllText((Join-Path $taskProject 'Packages/manifest.json'), '{"dependencies":{}}', [System.Text.UTF8Encoding]::new($false))
$taskLog = Join-Path $taskProject 'filter-smoke.log'
$taskArguments = @('-batchmode', '-projectPath', ('"' + $taskProject + '"'), '-executeMethod', 'CameraCaptureStudio.FilterSmoke.Run', '-logFile', ('"' + $taskLog + '"'))
$taskProcess = Start-Process -FilePath $UnityPath -ArgumentList $taskArguments -WindowStyle Hidden -PassThru -Wait
if ($taskProcess.ExitCode -ne 0) { throw "Unity validation failed ($($taskProcess.ExitCode)). See $taskLog" }
if (-not (Select-String -LiteralPath $taskLog -Pattern 'FILTER_SMOKE_PASS' -Quiet)) { throw "Unity did not report success. See $taskLog" }
Select-String -LiteralPath $taskLog -Pattern 'FILTER_SMOKE_PASS|BLUR_DETAIL|BLUR_SCALE_DIFFERENCE'
