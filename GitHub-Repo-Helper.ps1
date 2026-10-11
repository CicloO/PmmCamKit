# GitHub Repo Helper - Windows PowerShell 5.1 / Windows 11
# Save as UTF-8 with BOM for Chinese strings in Windows PowerShell 5.1.
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$script:RepoDir = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\','/')
$script:GitExe = $null
$script:Busy = $false
$script:Utf8 = New-Object System.Text.UTF8Encoding($false)
try { [Console]::OutputEncoding = $script:Utf8 } catch { }
$OutputEncoding = $script:Utf8

$form = New-Object System.Windows.Forms.Form
$form.Text = 'GitHub 仓库助手'
$form.StartPosition = 'CenterScreen'
$form.ClientSize = New-Object System.Drawing.Size(780, 604)
$form.MinimumSize = New-Object System.Drawing.Size(700, 550)
$form.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 9)
$form.AutoScaleMode = [System.Windows.Forms.AutoScaleMode]::Dpi

$lblPath = New-Object System.Windows.Forms.Label
$lblPath.AutoSize = $false
$lblPath.Location = New-Object System.Drawing.Point(18, 17)
$lblPath.Size = New-Object System.Drawing.Size(740, 47)
$lblPath.Anchor = 'Top,Left,Right'
$lblPath.Text = "当前仓库文件夹：`r`n$script:RepoDir"
$form.Controls.Add($lblPath)

$lblUrl = New-Object System.Windows.Forms.Label
$lblUrl.Text = 'GitHub 仓库 HTTPS 地址（粘贴 GitHub Quick setup 中的地址）'
$lblUrl.Location = New-Object System.Drawing.Point(18, 77)
$lblUrl.Size = New-Object System.Drawing.Size(730, 23)
$form.Controls.Add($lblUrl)

$txtUrl = New-Object System.Windows.Forms.TextBox
$txtUrl.Location = New-Object System.Drawing.Point(18, 105)
$txtUrl.Size = New-Object System.Drawing.Size(744, 28)
$txtUrl.Anchor = 'Top,Left,Right'
$txtUrl.Font = New-Object System.Drawing.Font('Consolas', 10)
$form.Controls.Add($txtUrl)

$lblMsg = New-Object System.Windows.Forms.Label
$lblMsg.Text = '提交说明（留空时自动使用时间）'
$lblMsg.Location = New-Object System.Drawing.Point(18, 152)
$lblMsg.Size = New-Object System.Drawing.Size(730, 22)
$form.Controls.Add($lblMsg)

$txtMsg = New-Object System.Windows.Forms.TextBox
$txtMsg.Location = New-Object System.Drawing.Point(18, 179)
$txtMsg.Size = New-Object System.Drawing.Size(744, 28)
$txtMsg.Anchor = 'Top,Left,Right'
$txtMsg.Text = ''
$form.Controls.Add($txtMsg)

$btnSetup = New-Object System.Windows.Forms.Button
$btnSetup.Text = '① 初始化并关联 GitHub'
$btnSetup.Location = New-Object System.Drawing.Point(18, 234)
$btnSetup.Size = New-Object System.Drawing.Size(235, 44)
$form.Controls.Add($btnSetup)

$btnPublish = New-Object System.Windows.Forms.Button
$btnPublish.Text = '② 一键提交并推送'
$btnPublish.Location = New-Object System.Drawing.Point(269, 234)
$btnPublish.Size = New-Object System.Drawing.Size(235, 44)
$form.Controls.Add($btnPublish)

$btnRefresh = New-Object System.Windows.Forms.Button
$btnRefresh.Text = '刷新仓库状态'
$btnRefresh.Location = New-Object System.Drawing.Point(520, 234)
$btnRefresh.Size = New-Object System.Drawing.Size(242, 44)
$btnRefresh.Anchor = 'Top,Right'
$form.Controls.Add($btnRefresh)

$lblInfo = New-Object System.Windows.Forms.Label
$lblInfo.Location = New-Object System.Drawing.Point(18, 293)
$lblInfo.Size = New-Object System.Drawing.Size(744, 40)
$lblInfo.Anchor = 'Top,Left,Right'
$lblInfo.Text = '状态：正在检查 Git...'
$form.Controls.Add($lblInfo)

$lblLog = New-Object System.Windows.Forms.Label
$lblLog.Text = '操作记录'
$lblLog.Location = New-Object System.Drawing.Point(18, 343)
$lblLog.Size = New-Object System.Drawing.Size(300, 22)
$form.Controls.Add($lblLog)

$txtLog = New-Object System.Windows.Forms.TextBox
$txtLog.Location = New-Object System.Drawing.Point(18, 369)
$txtLog.Size = New-Object System.Drawing.Size(744, 188)
$txtLog.Multiline = $true
$txtLog.ScrollBars = 'Both'
$txtLog.WordWrap = $false
$txtLog.ReadOnly = $true
$txtLog.Anchor = 'Top,Bottom,Left,Right'
$txtLog.Font = New-Object System.Drawing.Font('Consolas', 9)
$form.Controls.Add($txtLog)

$lblFooter = New-Object System.Windows.Forms.Label
$lblFooter.Location = New-Object System.Drawing.Point(18, 570)
$lblFooter.Size = New-Object System.Drawing.Size(744, 21)
$lblFooter.Anchor = 'Bottom,Left,Right'
$lblFooter.Text = '不会强制推送；提交前会让你审核变动文件。首次推送可能弹出 GitHub 浏览器登录。'
$form.Controls.Add($lblFooter)

function Write-Log {
    param([string]$Message)
    $time = Get-Date -Format 'HH:mm:ss'
    $txtLog.AppendText("[$time] $Message`r`n")
    $txtLog.SelectionStart = $txtLog.TextLength
    $txtLog.ScrollToCaret()
    $txtLog.Refresh()
}

function Set-Busy {
    param([bool]$Value)
    $script:Busy = $Value
    $btnSetup.Enabled = -not $Value
    $btnPublish.Enabled = -not $Value
    $btnRefresh.Enabled = -not $Value
    $form.UseWaitCursor = $Value
    $form.Refresh()
    [System.Windows.Forms.Application]::DoEvents()
}

function Show-Error {
    param([string]$Message)
    Write-Log ("错误：" + $Message)
    [void][System.Windows.Forms.MessageBox]::Show(
        $form, $Message, 'GitHub 仓库助手',
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error
    )
}

function Confirm {
    param([string]$Message, [string]$Title = '请确认')
    $choice = [System.Windows.Forms.MessageBox]::Show(
        $form, $Message, $Title,
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Question
    )
    return ($choice -eq [System.Windows.Forms.DialogResult]::Yes)
}

function Invoke-Git {
    param([string[]]$GitArguments, [switch]$Quiet)
    if ($null -eq $script:GitExe) {
        return [pscustomobject]@{ Success = $false; Output = '没有找到 git.exe，请先安装 Git for Windows。'; Code = 127 }
    }
    if (-not $Quiet) { Write-Log ("> git " + ($GitArguments -join ' ')) }
    try {
        $lines = @(& $script:GitExe -C $script:RepoDir @GitArguments 2>&1 | ForEach-Object { $_.ToString() })
        $exitCode = $LASTEXITCODE
        $output = ($lines -join "`r`n").Trim()
    } catch {
        $exitCode = 1
        $output = $_.Exception.Message
    }
    if (-not $Quiet -and $output.Length -gt 0) { Write-Log $output }
    return [pscustomobject]@{ Success = ($exitCode -eq 0); Output = $output; Code = $exitCode }
}

function Get-DesiredUrl {
    $url = $txtUrl.Text.Trim().TrimEnd('/')
    if ($url -notmatch '^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
        Show-Error '请输入完整的 GitHub HTTPS 仓库地址，例如：https://github.com/用户名/仓库名.git。不要输入网页的其他地址。'
        return $null
    }
    return $url
}

function Normalize-RemoteUrl {
    param([string]$Url)
    return (($Url.Trim().TrimEnd('/') -replace '\.git$', '').ToLowerInvariant())
}

function Test-RepoRoot {
    # Also prevents accidentally operating on a parent repo when this tool sits in a subfolder.
    $root = Invoke-Git -GitArguments @('rev-parse', '--show-toplevel') -Quiet
    if (-not $root.Success) { return $false }
    try {
        $gitRoot = [System.IO.Path]::GetFullPath($root.Output.Trim()).TrimEnd('\','/')
        return [string]::Equals($gitRoot, $script:RepoDir, [System.StringComparison]::OrdinalIgnoreCase)
    } catch { return $false }
}

function Test-ParentRepo {
    $root = Invoke-Git -GitArguments @('rev-parse', '--show-toplevel') -Quiet
    if (-not $root.Success) { return $false }
    if (Test-RepoRoot) { return $false }
    Show-Error ("本脚本所在文件夹不是 Git 仓库根目录。`r`n`r`n检测到仓库根目录：" + $root.Output + "`r`n`r`n请将两个工具文件放到该根目录后再运行。")
    return $true
}

function Initialize-Connect {
    $url = Get-DesiredUrl
    if ($null -eq $url) { return $false }
    if ($null -eq $script:GitExe) { Show-Error '找不到 Git。请先安装 Git for Windows 并重新打开工具。'; return $false }
    if (Test-ParentRepo) { return $false }

    $repoFound = Test-RepoRoot
    if (-not $repoFound) {
        Write-Log '该文件夹还没有 Git 仓库，准备执行 git init。'
        $init = Invoke-Git -GitArguments @('init')
        if (-not $init.Success) { Show-Error ('初始化失败：' + $init.Output); return $false }
    } else {
        Write-Log '检测到现有 Git 仓库，将保留已有历史。'
    }

    $origin = Invoke-Git -GitArguments @('remote', 'get-url', 'origin') -Quiet
    if ($origin.Success -and (Normalize-RemoteUrl $origin.Output) -ne (Normalize-RemoteUrl $url)) {
        if (-not (Confirm ("当前 origin 已关联：`r`n" + $origin.Output + "`r`n`r`n你输入的是：`r`n" + $url + "`r`n`r`n是否将 origin 改为新地址？") '修改远程仓库地址')) {
            Write-Log '已取消修改 origin。'
            return $false
        }
    }

    $branch = Invoke-Git -GitArguments @('branch', '--show-current') -Quiet
    if (-not $branch.Success -or [string]::IsNullOrWhiteSpace($branch.Output)) {
        Show-Error '无法确定当前分支（可能处于 detached HEAD 状态）。请先在 Git 中切换到正常分支。'
        return $false
    }
    if ($branch.Output.Trim() -ne 'main') {
        if ($repoFound -and -not (Confirm ("当前分支是 " + $branch.Output.Trim() + "。`r`n工具需要使用 main 分支。`r`n`r`n是否将当前分支重命名为 main？不会删除提交历史。") '分支名称确认')) {
            Write-Log '已取消分支重命名。'
            return $false
        }
        # -m does not overwrite an existing main branch. An unborn branch may use -M safely.
        $hasHead = Invoke-Git -GitArguments @('rev-parse', '--verify', 'HEAD') -Quiet
        if ($hasHead.Success) { $rename = Invoke-Git -GitArguments @('branch', '-m', 'main') }
        else { $rename = Invoke-Git -GitArguments @('branch', '-M', 'main') }
        if (-not $rename.Success) {
            Show-Error ('切换 main 分支失败（不会覆盖已有 main 分支）：' + $rename.Output)
            return $false
        }
    }

    if ($origin.Success) { $link = Invoke-Git -GitArguments @('remote', 'set-url', 'origin', $url) }
    else { $link = Invoke-Git -GitArguments @('remote', 'add', 'origin', $url) }
    if (-not $link.Success) { Show-Error ('关联 origin 失败：' + $link.Output); return $false }
    Write-Log ("已完成：main → " + $url)
    Refresh-Status
    [void][System.Windows.Forms.MessageBox]::Show(
        $form, '本地仓库已准备好并关联到 GitHub。现在可点击「一键提交并推送」。',
        '关联成功', [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information
    )
    return $true
}

function Refresh-Status {
    if ($null -eq $script:GitExe) {
        $lblInfo.Text = '状态：没有找到 Git for Windows。请安装 Git 后重启此工具。'
        return
    }
    if (Test-RepoRoot) {
        $branch = Invoke-Git -GitArguments @('branch', '--show-current') -Quiet
        $remote = Invoke-Git -GitArguments @('remote', 'get-url', 'origin') -Quiet
        $state = Invoke-Git -GitArguments @('status', '--short') -Quiet
        $branchName = if ($branch.Success) { $branch.Output.Trim() } else { '(未知)' }
        $remoteName = if ($remote.Success) { $remote.Output.Trim() } else { '(尚未关联)' }
        $count = if ($state.Success -and $state.Output.Length -gt 0) { @($state.Output -split "`r?`n").Count } else { 0 }
        $lblInfo.Text = "当前分支：$branchName    待处理文件：$count`r`norigin：$remoteName"
        if ($remote.Success -and [string]::IsNullOrWhiteSpace($txtUrl.Text)) { $txtUrl.Text = $remote.Output.Trim() }
        Write-Log '仓库状态已刷新。'
    } else {
        $lblInfo.Text = '状态：尚未建立此目录的独立 Git 仓库，或工具不在仓库根目录。'
    }
}

function Confirm-FileList {
    param([string]$Details)
    $dialog = New-Object System.Windows.Forms.Form
    $dialog.Text = '确认本次提交的文件'
    $dialog.StartPosition = 'CenterParent'
    $dialog.ClientSize = New-Object System.Drawing.Size(670, 440)
    $dialog.MinimumSize = New-Object System.Drawing.Size(560, 380)
    $dialog.Font = $form.Font
    $label = New-Object System.Windows.Forms.Label
    $label.Location = New-Object System.Drawing.Point(14, 12)
    $label.Size = New-Object System.Drawing.Size(640, 57)
    $label.Anchor = 'Top,Left,Right'
    $label.Text = "即将提交以下新增、修改和删除的文件，并推送到 GitHub。`r`n请确认没有密钥、私人文件或不适合公开的内容。"
    $dialog.Controls.Add($label)
    $list = New-Object System.Windows.Forms.TextBox
    $list.Location = New-Object System.Drawing.Point(14, 75)
    $list.Size = New-Object System.Drawing.Size(642, 298)
    $list.Anchor = 'Top,Bottom,Left,Right'
    $list.Multiline = $true
    $list.ScrollBars = 'Both'
    $list.WordWrap = $false
    $list.ReadOnly = $true
    $list.Font = New-Object System.Drawing.Font('Consolas', 9)
    $list.Text = $Details
    $dialog.Controls.Add($list)
    $btnOk = New-Object System.Windows.Forms.Button
    $btnOk.Text = '确认提交并推送'
    $btnOk.Location = New-Object System.Drawing.Point(321, 386)
    $btnOk.Size = New-Object System.Drawing.Size(160, 38)
    $btnOk.Anchor = 'Bottom,Right'
    $btnOk.DialogResult = [System.Windows.Forms.DialogResult]::OK
    $dialog.Controls.Add($btnOk)
    $btnCancel = New-Object System.Windows.Forms.Button
    $btnCancel.Text = '取消'
    $btnCancel.Location = New-Object System.Drawing.Point(496, 386)
    $btnCancel.Size = New-Object System.Drawing.Size(160, 38)
    $btnCancel.Anchor = 'Bottom,Right'
    $btnCancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $dialog.Controls.Add($btnCancel)
    $dialog.AcceptButton = $btnOk
    $dialog.CancelButton = $btnCancel
    try { return ($dialog.ShowDialog($form) -eq [System.Windows.Forms.DialogResult]::OK) }
    finally { $dialog.Dispose() }
}

function Publish-Changes {
    if ($null -eq $script:GitExe) { Show-Error '找不到 Git。请先安装 Git for Windows。'; return }
    if (Test-ParentRepo) { return }
    if (-not (Test-RepoRoot)) { Show-Error '还没有初始化仓库。请先输入 GitHub 地址并点击「初始化并关联」。'; return }
    $branch = Invoke-Git -GitArguments @('branch', '--show-current') -Quiet
    if (-not $branch.Success -or $branch.Output.Trim() -ne 'main') {
        Show-Error '当前分支不是 main。请先点击「初始化并关联」检查分支。'
        return
    }
    $origin = Invoke-Git -GitArguments @('remote', 'get-url', 'origin') -Quiet
    if (-not $origin.Success) { Show-Error '还没有关联 origin。请先点击「初始化并关联」。'; return }
    if (-not [string]::IsNullOrWhiteSpace($txtUrl.Text)) {
        $url = Get-DesiredUrl
        if ($null -eq $url) { return }
        if ((Normalize-RemoteUrl $origin.Output) -ne (Normalize-RemoteUrl $url)) {
            Show-Error '地址框与现有 origin 不一致。请先点击「初始化并关联」确认修改，避免推错仓库。'
            return
        }
    }

    $status = Invoke-Git -GitArguments @('status', '--short', '--untracked-files=all') -Quiet
    if (-not $status.Success) { Show-Error ('获取文件变更失败：' + $status.Output); return }
    if (-not [string]::IsNullOrWhiteSpace($status.Output)) {
        if (-not (Confirm-FileList $status.Output)) {
            Write-Log '用户取消提交，没有执行 git add。'
            return
        }
        $name = Invoke-Git -GitArguments @('config', 'user.name') -Quiet
        $email = Invoke-Git -GitArguments @('config', 'user.email') -Quiet
        if (-not $name.Success -or [string]::IsNullOrWhiteSpace($name.Output) -or -not $email.Success -or [string]::IsNullOrWhiteSpace($email.Output)) {
            Show-Error ("Git 还没有配置提交身份。请在 PowerShell 中执行：`r`n`r`ngit config --global user.name `"你的名字`"`r`ngit config --global user.email `"你的邮箱`"`r`n`r`n然后重试。")
            return
        }
        $add = Invoke-Git -GitArguments @('add', '--all')
        if (-not $add.Success) { Show-Error ('暂存文件失败：' + $add.Output); return }
        $msg = $txtMsg.Text.Trim()
        if ([string]::IsNullOrWhiteSpace($msg)) { $msg = 'Update ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') }
        $commit = Invoke-Git -GitArguments @('commit', '-m', $msg)
        if (-not $commit.Success) { Show-Error ('提交失败：' + $commit.Output); return }
    } else {
        Write-Log '当前没有文件变更，尝试推送尚未上传的本地提交。'
    }

    $head = Invoke-Git -GitArguments @('rev-parse', '--verify', 'HEAD') -Quiet
    if (-not $head.Success) { Show-Error '当前没有任何提交。请先向仓库文件夹添加文件。'; return }
    Write-Log '开始推送到 GitHub；第一次可能需要在浏览器中完成登录。'
    $push = Invoke-Git -GitArguments @('push', '-u', 'origin', 'main')
    if (-not $push.Success) {
        Show-Error ("推送失败。本地提交已保留，不会丢失。`r`n`r`n" + $push.Output + "`r`n`r`n若 GitHub 仓库已有 README 或其他提交，需要先合并远程历史；不要盲目使用 --force。")
        return
    }
    Refresh-Status
    [void][System.Windows.Forms.MessageBox]::Show(
        $form, '本地提交已成功推送到 GitHub。', '完成',
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information
    )
}

$btnSetup.Add_Click({
    if ($script:Busy) { return }
    Set-Busy $true
    try { [void](Initialize-Connect) }
    catch { Show-Error $_.Exception.Message }
    finally { Set-Busy $false }
})
$btnPublish.Add_Click({
    if ($script:Busy) { return }
    Set-Busy $true
    try { Publish-Changes }
    catch { Show-Error $_.Exception.Message }
    finally { Set-Busy $false }
})
$btnRefresh.Add_Click({
    if ($script:Busy) { return }
    Set-Busy $true
    try { Refresh-Status }
    catch { Show-Error $_.Exception.Message }
    finally { Set-Busy $false }
})

$gitCommand = Get-Command 'git.exe' -ErrorAction SilentlyContinue
if ($null -ne $gitCommand) {
    $script:GitExe = $gitCommand.Source
    Write-Log ('Git：' + $script:GitExe)
    Refresh-Status
} else {
    Write-Log '没有检测到 Git for Windows。请安装后重新运行。'
    $lblInfo.Text = '状态：未检测到 Git。请先安装 Git for Windows。'
}
[void][System.Windows.Forms.Application]::Run($form)
