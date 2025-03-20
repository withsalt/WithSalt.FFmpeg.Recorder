
param (
    [string]$BuildTarget = "./Demos/ConsoleAppDemo/ConsoleAppDemo.csproj",
    [string]$SourceePath = "./src",
    [string]$ProjectName = "ConsoleAppDemo",
    [string]$RemoteUser = "loong",
    [string]$RemoteHost = "192.168.188.25",
    [string]$RemoteRootPath = "/home/$RemoteUser/Projects/CSharpProject",
    [int]$RemotePort = 22,
    [switch]$UsePassword,
    [string]$BuildConfiguration = "Release",
    [string]$RuntimeIdentifier="linux-loongarch64",
    [int]$RetryCount = 3
)

# ---------------------
# 初始化环境
# ---------------------
$scriptDirectory = $PSScriptRoot
$logFilePath = Join-Path $scriptDirectory "build_log.log"
$RemoteProjectPath = "$RemoteRootPath/$ProjectName"

$global:ErrorActionPreference = 'Stop'

# ---------------------
# 日志函数（支持颜色和日志分级）
# ---------------------
function Write-Log {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Message,
        
        [ValidateSet('INFO','WARN','ERROR','SUCCESS')]
        [string]$Level = 'INFO',
        
        [switch]$NoNewLine
    )

    $colorMap = @{
        'INFO'    = 'White'
        'WARN'    = 'Yellow'
        'ERROR'   = 'Red'
        'SUCCESS' = 'Green'
    }

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    $logEntry = "$timestamp [$Level] $Message"
    
    # 控制台输出带颜色
    if ($Host.UI.RawUI) {
        $originalColor = $Host.UI.RawUI.ForegroundColor
        $Host.UI.RawUI.ForegroundColor = $colorMap[$Level]
        if ($NoNewLine) { Write-Host $logEntry -NoNewline }
        else { Write-Host $logEntry }
        $Host.UI.RawUI.ForegroundColor = $originalColor
    }
    else {
        Write-Output $logEntry
    }

    # 写入日志文件
    Add-Content -Path $logFilePath -Value $logEntry
}

# ---------------------
# 功能函数
# ---------------------
function Invoke-WithRetry {
    param(
        [scriptblock]$ScriptBlock,
        [int]$MaxRetries = 3,
        [int]$RetryDelay = 2
    )

    $attempt = 1
    do {
        try {
            return & $ScriptBlock
        }
        catch {
            if ($attempt -gt $MaxRetries) {
                Write-Log "操作在 $MaxRetries 次重试后失败" -Level ERROR
                throw
            }
            
            Write-Log "操作失败 (尝试 $attempt/$MaxRetries): $_" -Level WARN
            Start-Sleep -Seconds ($RetryDelay * $attempt)
            $attempt++
        }
    } while ($true)
}

# ---------------------
# 主程序逻辑
# ---------------------
try {
    # 模块管理
    if (!(Get-Module -ListAvailable -Name Posh-SSH)) {
        Write-Log "安装 Posh-SSH 模块..." -Level INFO
        Install-Module -Name Posh-SSH -Force -Scope CurrentUser -SkipPublisherCheck
    }
    Import-Module Posh-SSH

    Get-SSHTrustedHost | Remove-SSHTrustedHost | Out-Null

    # 准备临时目录
    $zipFileName="$ProjectName.zip"
    $tmpDirectory = Join-Path $scriptDirectory "tmp"
    $zipFilePath = Join-Path $tmpDirectory $zipFileName

    # 创建干净的临时目录
    Invoke-WithRetry -ScriptBlock {
        New-Item -Path $tmpDirectory -ItemType Directory -Force | Out-Null
        Write-Log "临时目录创建成功: $tmpDirectory" -Level SUCCESS
    }

    # 智能项目打包
    Write-Log "开始项目打包（排除开发目录）..." -Level INFO
    $stagingDir = Join-Path $tmpDirectory "staging"
    
    try {
        # 使用 robocopy 进行高效复制和排除
        $excludeDirs = @('bin', 'obj', '.vs', '.git', 'tmp', 'packages')
        $robocopyArgs = @($SourceePath, $stagingDir, '/MIR', '/NFL', '/NDL', '/NJH', '/NJS', "/XD", $excludeDirs)
        $robocopyLog = robocopy @robocopyArgs 2>&1
        
        if ($LASTEXITCODE -ge 8) {
            throw "文件复制失败 (ExitCode: $LASTEXITCODE)"
        }
        
        Write-Log "项目文件准备完成，有效文件数: $((Get-ChildItem $stagingDir -Recurse | Measure-Object).Count)" -Level SUCCESS
    }
    catch {
        Write-Log "项目打包失败: $_" -Level ERROR
        throw
    }

    # 创建压缩包
    Invoke-WithRetry -ScriptBlock {
        Compress-Archive -Path "$stagingDir/*" -DestinationPath $zipFilePath -CompressionLevel Optimal
        Write-Log "压缩包创建成功，大小: $('{0:N2} MB' -f ((Get-Item $zipFilePath).Length/1MB))" -Level SUCCESS
    }

    # SSH 认证
    if ($UsePassword) {
        $credential = Get-Credential -UserName $RemoteUser -Message "请输入远程主机密码"
    }
    else {
        $credential = New-Object System.Management.Automation.PSCredential (
            $RemoteUser,
            (New-Object System.Security.SecureString)
        )
    }

    # SSH 连接管理
    try {
        Write-Log "正在建立SSH连接 [$RemoteHost]:$RemotePort..." -Level INFO
        $session = New-SSHSession -ComputerName $RemoteHost -Port $RemotePort -Credential $credential -AcceptKey -ConnectionTimeout 30 
    
        if (-not $session.Connected) {
            throw "SSH连接失败"
        }
        Write-Log "SSH连接成功" -Level SUCCESS
    } catch {
        Write-Log "无法建立 SSH 会话：$_" "ERROR"
        exit 1
    }

    # 远程操作
    try {
        # 环境检查
        $requiredTools = @('unzip', 'dotnet')
        foreach ($tool in $requiredTools) {
            $checkResult = Invoke-SSHCommand -SessionId $session.SessionId -Command "command -v $tool"
            if ($checkResult.ExitStatus -ne 0) {
                throw "缺少必要工具: $tool"
            }
        }

        # 检查、删除或创建远程目录
        $checkDirCmd = "if [ -d '$RemoteProjectPath' ]; then echo 'exists'; else echo 'not_exists'; fi"
        $checkDirResult = Invoke-SSHCommand -SessionId $session.SessionId -Command $checkDirCmd

        if ($checkDirResult.Output[0] -eq 'exists') {
            $removeDirCmd = "rm -rf '$RemoteProjectPath'"
            Invoke-SSHCommand -SessionId $session.SessionId -Command $removeDirCmd | Out-Null
        }
        $createDirCmd = "mkdir -p '$RemoteProjectPath'"
        Invoke-SSHCommand -SessionId $session.SessionId -Command $createDirCmd | Out-Null

        # 文件传输
        Invoke-WithRetry -ScriptBlock {
            Write-Log "开始安全文件传输..." -Level INFO
            Set-SCPItem -ComputerName $RemoteHost -Port $RemotePort -Credential $credential `
                        -Path $zipFilePath -Destination $RemoteRootPath -Verbose
            Write-Log "文件传输完成" -Level SUCCESS
        }

        # 获取远程已安装的 SDK 版本
        $sdkListResult = Invoke-SSHCommand -SessionId $session.SessionId -Command "dotnet --list-sdks"
        $installedSdks = $sdkListResult.Output | ForEach-Object { 
            ($_.Trim() -replace '\s+.*') -replace '^(\d+\.\d+).*', '$1'
        }

        if ($installedSdks.Count -gt 0) {
            # 选择 SDK 版本 (如果有多个，选择最新的主次版本)
            $selectedSdkVersion = $installedSdks | Sort-Object -Descending | Select-Object -First 1
            Write-Log "远程已安装的 .NET SDK 版本: $($installedSdks -join ', ')" -Level INFO
            Write-Log "将使用 .NET SDK 版本: $selectedSdkVersion" -Level INFO
        }
        else {
            Write-Log "远程未找到已安装的 .NET SDK。" -Level ERROR
            throw "远程未找到已安装的 .NET SDK。"
        }

        # 设置不同版本的参数
        if (($installedSdks.Count -eq 1) -and ($selectedSdkVersion -eq "9.0")) {
            $selectedSdkParam = "-p:NET9ONLY=True"
        }
        if (($installedSdks.Count -eq 1) -and ($selectedSdkVersion -eq "8.0")) {
            $selectedSdkParam = "-p:NET8ONLY=True"
        }
        if (($installedSdks.Count -eq 1) -and ($selectedSdkVersion -eq "7.0")) {
            $selectedSdkParam = "-p:NET7ONLY=True"
        }
        if (($installedSdks.Count -eq 1) -and ($selectedSdkVersion -eq "6.0")) {
            $selectedSdkParam = "-p:NET6ONLY=True"
        }

        # 远程解压和构建（新增版本检测逻辑）
        $remoteCommands = @(
            "cd $RemoteRootPath",
            "unzip -q -d $RemoteProjectPath -o $zipFileName",
            "rm -f $zipFileName",
            "cd $RemoteProjectPath"
            "dotnet build `"$BuildTarget`" --configuration $BuildConfiguration -p:RuntimeIdentifier=$RuntimeIdentifier -p:TargetFramework=net$selectedSdkVersion $selectedSdkParam"
        )

        Write-Log "开始构建 $BuildTarget" -Level INFO
        Write-Log $remoteCommands[4] -Level INFO
       
        $commandResult = Invoke-SSHCommand -SessionId $session.SessionId -Command ($remoteCommands -join ' && ')        
        if ($commandResult.ExitStatus -ne 0) {
            throw "远程构建失败: $($commandResult.Error)"
        }
        
        Write-Log "远程构建成功! 输出信息:....`n$($commandResult.Output[-5..-1] -join "`n")" -Level SUCCESS
    }
    finally {
        if ($session.Connected) {
            Remove-SSHSession -SessionId $session.SessionId | Out-Null
            Write-Log "SSH连接已安全关闭" -Level INFO
        }
    }
}
catch {
    Write-Log "$_" -Level ERROR
    exit 1
}
finally {
    # 资源清理
    if (Test-Path $tmpDirectory) {
        try {
            Remove-Item -Path $tmpDirectory -Recurse -Force
            Write-Log "已清理临时资源" -Level INFO
        }
        catch {
            Write-Log "临时目录清理失败: $_" -Level WARN
        }
    }
    Write-Log "操作日志已保存至: $logFilePath" -Level INFO
}
