# Read-only, one-shot comparison of the SpringRO client packet buffer and Npcap samples.
# This tool does not inject code, attach a debugger or modify the game process.
param(
    [int]$TargetProcessId = 0,
    [int]$Seconds = 90,
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
if ($Seconds -lt 5 -or $Seconds -gt 180) { throw 'Seconds must be between 5 and 180.' }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class SpringRoReadOnlyMemory {
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReadProcessMemory(IntPtr process, IntPtr address,
        [Out] byte[] buffer, UIntPtr size, out UIntPtr bytesRead);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr handle);
}
'@

if ($TargetProcessId -ne 0) {
    $game = Get-Process -Id $TargetProcessId -ErrorAction Stop
} else {
    $candidates = @(Get-Process -Name SpringRO -ErrorAction SilentlyContinue)
    if ($candidates.Count -ne 1) {
        throw "Expected one running SpringRO.exe; found $($candidates.Count). Supply -TargetProcessId if multiple clients are running."
    }
    $game = $candidates[0]
}

$module = $game.MainModule
if ([IO.Path]::GetFileName($module.FileName) -ine 'SpringRO.exe') {
    throw "PID $($game.Id) does not have SpringRO.exe as its main module."
}
$expectedSha256 = '591e3776e40ac3661a1c60576a145e34e69a61423a890fda292c955d237f91c2'
if ((Get-FileHash -LiteralPath $module.FileName -Algorithm SHA256).Hash -ine $expectedSha256) {
    throw 'The running SpringRO.exe differs from the analyzed executable. The packet buffer address may have changed.'
}

# Preferred VA 0x015E8198, image base 0x00400000. The packet dispatcher passes
# this global buffer to handlers for 0x0983 and 0x0196.
$packetAddress = [IntPtr]::Add($module.BaseAddress, 0x011E8198)
$handle = [SpringRoReadOnlyMemory]::OpenProcess(0x1010, $false, $game.Id)
if ($handle -eq [IntPtr]::Zero) {
    $code = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    throw "Windows denied read-only access to the game process (error $code). Do not retry by changing protections."
}

try {
    if (-not $OutputPath) {
        $diagnostics = Join-Path $env:LOCALAPPDATA 'RoAura-Dev\Diagnostics'
        [IO.Directory]::CreateDirectory($diagnostics) | Out-Null
        $OutputPath = Join-Path $diagnostics ('SpringRoClientStatus-{0:yyyyMMdd-HHmmss}.jsonl' -f [DateTime]::Now)
    }
    $buffer = New-Object byte[] 29
    $until = [DateTime]::UtcNow.AddSeconds($Seconds)
    $nextUpdate = [DateTime]::UtcNow.AddSeconds(10)
    $lastHex = ''
    $count = 0
    $reads = 0
    $writer = New-Object IO.StreamWriter($OutputPath, $false, [Text.Encoding]::UTF8)
    Write-Host "Watching SpringRO PID $($game.Id) for up to $Seconds seconds. Apply the status now."
    Write-Host "Local output: $OutputPath"
    try {
        while ([DateTime]::UtcNow -lt $until -and $count -lt 32 -and -not $game.HasExited) {
            [UIntPtr]$bytesRead = [UIntPtr]::Zero
            $readOk = [SpringRoReadOnlyMemory]::ReadProcessMemory($handle, $packetAddress,
                $buffer, [UIntPtr]::new(29), [ref]$bytesRead)
            if (-not $readOk -or $bytesRead.ToUInt32() -ne 29) {
                $code = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
                throw "Packet buffer became unreadable (Windows error $code); stopping."
            }
            $reads++
            $opcode = [BitConverter]::ToUInt16($buffer, 0)
            if ($opcode -eq 0x0983 -or $opcode -eq 0x0196) {
                $length = if ($opcode -eq 0x0983) { 29 } else { 9 }
                $hex = [BitConverter]::ToString($buffer, 0, $length).Replace('-', '')
                $now = [DateTime]::UtcNow
                if ($hex -ne $lastHex) {
                    $record = @{ observedAtUtc = $now.ToString('o'); opcode = ('0x{0:X4}' -f $opcode); hex = $hex }
                    $writer.WriteLine(($record | ConvertTo-Json -Compress))
                    $writer.Flush()
                    $lastHex = $hex
                    $count++
                    Write-Host "Status frame captured: $count"
                }
            } else {
                $lastHex = ''
            }
            if ([DateTime]::UtcNow -ge $nextUpdate) {
                Write-Host "Still watching; $count frames captured."
                $nextUpdate = [DateTime]::UtcNow.AddSeconds(10)
            }
            Start-Sleep -Milliseconds 1
        }
    } finally { $writer.Dispose() }
    Write-Host "Captured $count status frames in $reads reads: $OutputPath"
    if ($count -eq 0) { Write-Warning 'No status frame was observed. This does not imply that packet fields are unchanged.' }
} finally {
    [void][SpringRoReadOnlyMemory]::CloseHandle($handle)
}
