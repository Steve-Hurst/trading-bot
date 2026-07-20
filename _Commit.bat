@echo off
setlocal
echo Committing %~dp0...
cd /d "%~dp0"
git add -A
git commit -m "Auto-commit"
git push
echo Hydrating code warehouse...
"C:\Batch\Shared\CodeScrapper.exe" -bl-actions="hyrdateCodebaseWarehouseFolders" -folders="%CD%"
endlocal
