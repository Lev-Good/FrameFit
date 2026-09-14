# בדיקת קבלה למתקין: התקנה שקטה, אימות, הרצת הקובץ שהותקן, והסרה מלאה.
#
# שימוש:
#   pwsh -File installer/test-install.ps1 -Msi installer/bin/x64/Release/FrameFit-v0.1.0-x64.msi
#
# הקובץ מועתק לתיקיית עבודה בנתיב ASCII קבוע (C:\Users\Public), ולא ל-%TEMP%.
# שני נימוקים: (1) msiexec וקידוד הנתיבים נכשלים בשקט כשהנתיב מכיל תווים עבריים;
# (2) ‏%TEMP% מוחזר לעיתים בצורת שם קצר (8.3) בפרופיל עם שם משתמש שאינו ASCII,
# ואז PowerShell 5.1 מחזיר שגיאות-שווא ב-Copy-Item וב-Remove-Item.

param(
    [Parameter(Mandatory = $true)]
    [string]$Msi
)

$ErrorActionPreference = 'Continue'

# איתור קובץ המתקין: קודם כפי שהתקבל (יחסית לתיקיית העבודה), ואם לא נמצא —
# יחסית לשורש המאגר (תיקיית האב של תיקיית הסקריפט).
$MsiPath = $Msi
if (-not (Test-Path -LiteralPath $MsiPath)) {
    $fromRepoRoot = Join-Path (Split-Path -Parent $PSScriptRoot) $Msi
    if (Test-Path -LiteralPath $fromRepoRoot) { $MsiPath = $fromRepoRoot }
}

if (-not (Test-Path -LiteralPath $MsiPath)) {
    Write-Output "לא נמצא קובץ מתקין: $Msi"
    Write-Output "תיקיית העבודה: $(Get-Location)"
    exit 2
}

$MsiPath = (Resolve-Path -LiteralPath $MsiPath).Path
Write-Output "קובץ מתקין: $MsiPath"

$workDir = Join-Path $env:PUBLIC 'FrameFitTest'
$null = New-Item -ItemType Directory -Path $workDir -Force

$msi = Join-Path $workDir 'FrameFitTest.msi'
Remove-Item -LiteralPath $msi -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath $MsiPath -Destination $msi -Force

$log = Join-Path $workDir 'framefit-install.log'
$ulog = Join-Path $workDir 'framefit-uninstall.log'
$installDir = Join-Path $env:LOCALAPPDATA 'FrameFit'
$exe = Join-Path $installDir 'FrameFit.exe'
$lnk = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\FrameFit\FrameFit.lnk'
$selftestOut = Join-Path $workDir 'framefit-installed-selftest.txt'
$failures = @()

Write-Output "=== 1. התקנה שקטה ==="
$p = Start-Process msiexec.exe -ArgumentList "/i `"$msi`" /qn /norestart /l*v `"$log`"" -Wait -PassThru
Write-Output "קוד יציאה: $($p.ExitCode)"
if ($p.ExitCode -ne 0) { $failures += "ההתקנה החזירה קוד $($p.ExitCode)" }

Write-Output ""
Write-Output "=== 2. הקבצים שהותקנו ==="
if (Test-Path $installDir) {
    Get-ChildItem $installDir | ForEach-Object { Write-Output ("  {0}  {1:N1} MB" -f $_.Name, ($_.Length / 1MB)) }
} else {
    Write-Output "  התיקייה לא נוצרה"
    $failures += "תיקיית ההתקנה לא נוצרה"
}

Write-Output ""
Write-Output "=== 3. קיצור דרך בתפריט התחל ==="
$lnkExists = Test-Path $lnk
Write-Output "  קיים: $lnkExists"
if ($lnkExists) {
    $sh = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk)
    Write-Output "  מפנה אל: $($sh.TargetPath)"
    if ($sh.TargetPath -ne $exe) { $failures += "הקיצור מפנה אל $($sh.TargetPath) במקום אל $exe" }
} else {
    $failures += "קיצור הדרך לא נוצר"
}

Write-Output ""
Write-Output "=== 4. רשומת ההתקנה ברישום ==="
$reg = Get-ItemProperty 'HKCU:\Software\Lev-Good\FrameFit' -ErrorAction SilentlyContinue
Write-Output "  תיקיית התקנה: $($reg.InstallFolder)"
if (-not $reg) { $failures += "רשומת הרישום לא נוצרה" }

Write-Output ""
Write-Output "=== 5. הרצת הקובץ המותקן (בדיקה עצמית) ==="
if (Test-Path $exe) {
    if (Test-Path -LiteralPath $selftestOut) { Remove-Item -LiteralPath $selftestOut -Force }
    & $exe --self-test --no-flash --out=$selftestOut | Out-Null
    $selfExit = $LASTEXITCODE
    Start-Sleep -Seconds 2
    if (Test-Path $selftestOut) {
        $tail = Get-Content $selftestOut | Select-Object -Last 6
        $tail | ForEach-Object { Write-Output "  $_" }
        Write-Output "  קוד היציאה של הבדיקה העצמית: $selfExit"
        if ($selfExit -ne 0) { $failures += "הבדיקה העצמית של הקובץ המותקן נכשלה (קוד $selfExit)" }
    } else {
        Write-Output "  לא נוצר פלט בדיקה"
        $failures += "הקובץ המותקן לא הפיק דוח בדיקה עצמית"
    }
} else {
    Write-Output "  קובץ ההרצה לא נמצא"
    $failures += "קובץ ההרצה שהותקן לא נמצא"
}

Write-Output ""
Write-Output "=== 6. הסרה שקטה ==="
$p2 = Start-Process msiexec.exe -ArgumentList "/x `"$msi`" /qn /norestart /l*v `"$ulog`"" -Wait -PassThru
Write-Output "קוד יציאה: $($p2.ExitCode)"
if ($p2.ExitCode -ne 0) { $failures += "ההסרה החזירה קוד $($p2.ExitCode)" }

Start-Sleep -Seconds 2

Write-Output ""
Write-Output "=== 7. מצב אחרי הסרה ==="
$dirLeft = Test-Path $installDir
$lnkLeft = Test-Path $lnk
$regLeft = Test-Path 'HKCU:\Software\Lev-Good\FrameFit'
$settingsLeft = Test-Path (Join-Path $env:APPDATA 'FrameFit')
Write-Output "  תיקיית ההתקנה קיימת: $dirLeft"
Write-Output "  קיצור הדרך קיים: $lnkLeft"
Write-Output "  רשומת הרישום קיימת: $regLeft"
Write-Output "  הגדרות המשתמש נשמרו: $settingsLeft"

if ($dirLeft) { $failures += "תיקיית ההתקנה נותרה אחרי ההסרה" }
if ($lnkLeft) { $failures += "קיצור הדרך נותר אחרי ההסרה" }
if ($regLeft) { $failures += "רשומת הרישום נותרה אחרי ההסרה" }
if (-not $settingsLeft) { Write-Output "  (שים לב: תיקיית ההגדרות אינה קיימת — ייתכן שהתוכנה לא הורצה מעולם)" }

Write-Output ""
if ($failures.Count -eq 0) {
    Write-Output "=== התוצאה: כל בדיקות הקבלה עברו ==="
    exit 0
} else {
    Write-Output "=== התוצאה: נכשלו $($failures.Count) בדיקות ==="
    $failures | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
