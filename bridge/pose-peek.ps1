# Reads OXRMC's motionRigPose shared-memory file and prints it live.
# Run this (normal PowerShell, no admin) WHILE the bridge is running, to confirm
# the bridge is writing real, changing roll/pitch. No VR needed.
$name = "motionRigPose"
try {
    $mmf = [System.IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting($name)
} catch {
    Write-Host "Could not open '$name'. Start the bridge (run-bridge.bat) first," -ForegroundColor Yellow
    Write-Host "then re-run this. (OXRMC or the bridge must have created the file.)"
    exit 1
}
$acc = $mmf.CreateViewAccessor(0, 48)
$rad2deg = 180.0 / [Math]::PI
Write-Host "Reading $name  (Ctrl+C to stop). Drive / move the rig and watch roll & pitch." -ForegroundColor Cyan
Write-Host ("{0,-10} {1,10} {2,10} {3,10} {4,10} {5,10} {6,10}" -f "time","sway_m","surge_m","heave_m","yaw_deg","roll_deg","pitch_deg")
while ($true) {
    $v = New-Object double[] 6
    for ($i = 0; $i -lt 6; $i++) { $v[$i] = $acc.ReadDouble($i * 8) }
    Write-Host ("{0,-10} {1,10:F4} {2,10:F4} {3,10:F4} {4,10:F3} {5,10:F3} {6,10:F3}" -f `
        (Get-Date -Format HH:mm:ss), $v[0], $v[1], $v[2], ($v[3]*$rad2deg), ($v[4]*$rad2deg), ($v[5]*$rad2deg))
    Start-Sleep -Milliseconds 200
}
