# Sound the Spire uninstall: irm https://raw.githubusercontent.com/MuGdxy/SoundTheSpire/main/scripts/uninstall.ps1 | iex
# Runs install.ps1 -Uninstall; extra arguments (e.g. -GamePath) are passed through.
& ([scriptblock]::Create((Invoke-RestMethod https://raw.githubusercontent.com/MuGdxy/SoundTheSpire/main/scripts/install.ps1))) -Uninstall @args
