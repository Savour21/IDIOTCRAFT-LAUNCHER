@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$root = [IO.Path]::GetFullPath('%~dp0'); $exe = Get-ChildItem -LiteralPath $root -Recurse -File -Filter 'IDIOTCORDLauncher.exe' | Where-Object { $_.FullName -notmatch '\\(obj|publish|publish-0\.1\.0)\\' } | Sort-Object LastWriteTime -Descending | Select-Object -First 1; if ($null -eq $exe) { [Console]::Error.WriteLine('No built IDIOTCORDLauncher.exe was found. Build the project first.'); exit 1 }; Start-Process -FilePath $exe.FullName"
if errorlevel 1 (
  echo.
  echo No launcher build was found. Run a Debug or Release build first.
  pause
)
endlocal
