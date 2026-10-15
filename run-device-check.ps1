param(
    [ValidateSet('Background', 'Kill', 'All')]
    [string]$Scenario = 'All',
    [int]$BytesPerSecond = 4096,
    [switch]$SkipBuild
)

# Checks built-in catalog reading, background download, and resume after the app is killed on a connected Android device.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'TestProject'
$apk = Join-Path $project 'Builds/DeviceCheck.apk'
$package = 'com.h.contentsloader.devicecheck'
$port = 8765
$editor = (& unity editors --json | ConvertFrom-Json).data | Where-Object { $_.version -eq '6000.3.18f1' } | Select-Object -First 1
$adb = Join-Path (Split-Path $editor.location) 'Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe'
$work = Join-Path $root 'TestResults/device'
New-Item -ItemType Directory -Force $work | Out-Null

function Adb { & $adb @args }

function Wait-Log([string]$pattern, [int]$timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        # 화면이 꺼지면 앱이 멈춰서 결과가 안 나옴
        Adb shell input keyevent KEYCODE_WAKEUP
        $hit = Adb logcat -d -s Unity | Select-String '\[DeviceCheck\]' | Where-Object { $_ -match $pattern } | Select-Object -Last 1
        if ($hit) { return $hit.Line }
        Start-Sleep -Milliseconds 500
    }
    throw "Timed out waiting for '$pattern'"
}

function Show-DeviceLog {
    Adb logcat -d -s Unity | Select-String '\[DeviceCheck\]' | ForEach-Object { '  device: ' + ($_.Line -replace '^.*\[DeviceCheck\] ', '') }
}

function Start-App {
    Adb shell monkey -p $package -c android.intent.category.LAUNCHER 1 | Out-Null
}

function Reset-App {
    Adb shell am force-stop $package | Out-Null
    Adb shell pm clear $package | Out-Null
    Adb logcat -c
}

# 데몬을 출력 파이프와 분리해서 띄움. 안 그러면 데몬이 파이프를 물고 있어서 호출한 쪽이 끝나지 않음
Start-Process $adb -ArgumentList 'start-server' -WindowStyle Hidden
Start-Sleep 3

if (-not $SkipBuild) {
    & unity run $project --timeout 3000 --no-tail -l (Join-Path $work 'build.log') --json -- -buildTarget Android -executeMethod DeviceCheckBuild.BuildAndroid | Out-Null
    if (-not (Test-Path $apk)) { throw 'APK build failed. See TestResults/device/build.log' }
}

Adb install -r $apk | Out-Null
Adb reverse "tcp:$port" "tcp:$port" | Out-Null
$serverLog = Join-Path $work 'server.log'
$server = Start-Process python -ArgumentList @('-I', (Join-Path $root 'tools/range_server.py'), (Join-Path $project 'ContentsBuild/Android/Remote'), $port, $BytesPerSecond) `
    -RedirectStandardOutput $serverLog -RedirectStandardError (Join-Path $work 'server.err') -PassThru -WindowStyle Hidden

$failed = 0
try {
    if ($Scenario -in 'Background', 'All') {
        Write-Host '[Background] go home right after the download starts, come back after the server finished sending'
        Reset-App
        Start-App
        Wait-Log 'builtin ok' 30 | Out-Null
        Wait-Log 'progress 0\.00' 30 | Out-Null
        Start-Sleep 1
        Adb shell input keyevent KEYCODE_HOME
        Wait-Log 'paused=True' 10 | Out-Null
        # 앱이 멈춘 동안 서버 전송이 끝나야 OS가 대신 받은 것임
        $deadline = (Get-Date).AddSeconds(120)
        while (-not (Select-String -Path $serverLog -Pattern 'sent=(\d+)/\1' -Quiet) -and (Get-Date) -lt $deadline) { Start-Sleep 1 }
        $backgroundDone = Select-String -Path $serverLog -Pattern 'sent=(\d+)/\1' -Quiet
        Start-App
        $result = Wait-Log 'dragon (ok|FAIL)|download FAIL' 60
        Show-DeviceLog
        $ok = $backgroundDone -and $result -match 'dragon ok'
        Write-Host "[Background] backgroundComplete=$backgroundDone result=$result" -ForegroundColor ($(if ($ok) { 'Green' } else { 'Red' }))
        if (-not $ok) { $failed++ }
    }

    if ($Scenario -in 'Kill', 'All') {
        Write-Host '[Kill] kill the app process during the download, then launch it again'
        $serverLinesBefore = @(Get-Content $serverLog).Count
        Reset-App
        Start-App
        Wait-Log 'progress 0\.00' 30 | Out-Null
        Start-Sleep 1
        Adb shell input keyevent KEYCODE_HOME
        Wait-Log 'paused=True' 10 | Out-Null
        # OS가 백그라운드 앱을 정리하는 것과 같은 경로로 프로세스만 죽임. force-stop은 앱을 정지 상태로 만들어 다른 상황임
        for ($i = 0; $i -lt 20 -and (Adb shell pidof $package); $i++) {
            Adb shell am kill $package | Out-Null
            Start-Sleep -Milliseconds 500
        }
        $alive = Adb shell pidof $package
        Adb logcat -c
        Start-App
        $remaining = Wait-Log 'remaining' 30
        $result = Wait-Log 'dragon (ok|FAIL)|download FAIL' 120
        Show-DeviceLog
        Start-Sleep 1
        $requests = @(Get-Content $serverLog | Select-Object -Skip $serverLinesBefore | Where-Object { $_ -match '^GET' })
        # 이전 실행이 남긴 DownloadManager 재시도 요청은 Range가 붙어 있어서 처음부터 받은 요청만 셈
        $fresh = @($requests | Where-Object { $_ -match 'range=None' }).Count
        $ok = -not $alive -and $result -match 'dragon ok' -and $fresh -le 1
        $requests | ForEach-Object { "  server: $_" }
        Write-Host "[Kill] killed=$(-not $alive) $remaining freshRequests=$fresh result=$result" -ForegroundColor ($(if ($ok) { 'Green' } else { 'Red' }))
        if (-not $ok) { $failed++ }
    }
}
finally {
    Stop-Process -Id $server.Id -ErrorAction SilentlyContinue
    Adb reverse --remove "tcp:$port" | Out-Null
    Write-Host 'server log:'
    Get-Content $serverLog | ForEach-Object { "  $_" }
}

exit $failed
