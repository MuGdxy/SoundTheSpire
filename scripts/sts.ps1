# Sends one dev-console command to the running game (Debug build of SoundTheSpire) and prints the reply.
# Example: .\scripts\sts.ps1 sts_state
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)]
    [string[]]$Command,
    [int]$Port = 47800
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$client = New-Object System.Net.Sockets.TcpClient
try {
    $client.Connect("127.0.0.1", $Port)
} catch {
    Write-Error "Cannot reach the game on port $Port. Is it running with a Debug build of SoundTheSpire?"
    exit 2
}

$stream = $client.GetStream()
$utf8 = New-Object System.Text.UTF8Encoding($false)
$writer = New-Object System.IO.StreamWriter($stream, $utf8)
$writer.WriteLine(($Command -join " "))
$writer.Flush()
$reply = (New-Object System.IO.StreamReader($stream, $utf8)).ReadToEnd()
$client.Close()

Write-Output $reply
if ($reply.StartsWith("fail:")) { exit 1 }
