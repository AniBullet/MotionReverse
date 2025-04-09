@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul
echo NieR 动作文件名转换工具
echo ---------------------------
echo.

:: 选择文件夹对话框
echo 请选择要处理的文件夹...
set "psCommand=powershell -Command "Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.FolderBrowserDialog; $f.Description = '选择要处理的文件夹'; $f.RootFolder = 'Desktop'; if($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { $f.SelectedPath } else { exit }""
for /f "usebackq delims=" %%f in (`%psCommand%`) do set "folder=%%f"

if "%folder%"=="" (
    echo 已取消操作。
    goto :end
)

:: 创建目标文件夹
set "newFolder=%folder%_new"
if not exist "%newFolder%" mkdir "%newFolder%"

echo.
echo 原始文件夹: %folder%
echo 目标文件夹: %newFolder%
echo.
echo 开始处理文件...

set /a total=0
set /a success=0

:: 处理每个文件
for %%F in ("%folder%\*.*") do (
    set /a total+=1
    set "fullName=%%~nxF"
    set "fileName=%%~nF"
    set "ext=%%~xF"
    
    :: 使用PowerShell提取pl部分和十六进制部分
    for /f "usebackq delims=" %%P in (`powershell -command "$filename = '!fileName!'; if($filename -match '(pl\d+)_(\w+)') { $plPart = $matches[1]; $hexPart = $matches[2]; Write-Output $plPart; Write-Output $hexPart; Write-Output 'found' } else { Write-Output 'notfound' }"`) do (
        if not defined plPart (
            set "plPart=%%P"
        ) else if not defined hexPart (
            set "hexPart=%%P"
        ) else (
            set "status=%%P"
        )
    )
    
    if "!status!"=="found" (
        :: 尝试转换十六进制
        for /f "usebackq delims=" %%H in (`powershell -command "try { $hex = '!hexPart!'; $decimal = [Convert]::ToInt32($hex, 16); $formatted = $decimal.ToString('D5'); Write-Output $formatted; Write-Output 'success' } catch { Write-Output 'failed' }"`) do (
            if not defined result (
                set "result=%%H"
            ) else (
                set "hexStatus=%%H"
            )
        )
        
        if "!hexStatus!"=="success" (
            set /a success+=1
            
            :: 构建新文件名 (格式: plxxxx_xxxxx.ext)
            set "newName=!plPart!_!result!"
            
            echo [成功] !fullName! -^> !newName!!ext!
            copy "%%F" "!newFolder!\!newName!!ext!" > nul
        ) else (
            echo [保留] !fullName! （无法转换十六进制数）
            copy "%%F" "!newFolder!\!fullName!" > nul
        )
    ) else (
        echo [保留] !fullName! （找不到有效的格式）
        copy "%%F" "!newFolder!\!fullName!" > nul
    )
    
    :: 清除变量，准备下一个循环
    set "plPart="
    set "hexPart="
    set "result="
    set "hexStatus="
    set "status="
)

echo.
echo 处理完成！
echo ---------------------------
echo 总文件数: !total!
echo 成功转换: !success!
echo.

:: 询问是否打开目标文件夹
set /p openFolder=是否打开目标文件夹？(Y/N): 
if /i "!openFolder!"=="Y" start "" "!newFolder!"

:end
echo.
echo 按任意键退出...
pause > nul 