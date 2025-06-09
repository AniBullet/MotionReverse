-- MotionReverse Tool
-- 作者: AniBullet
-- 版本: 1.0.0
-- 描述: MotionReverse脚本管理工具

rollout MotionReverseTool "MotionReverse 工具" width:400 height:600
(
    -- UI控件
    dotNetControl scriptList "System.Windows.Forms.ListView" pos:[10,10] width:380 height:400
    dotNetControl descriptionBox "System.Windows.Forms.TextBox" pos:[10,420] width:380 height:60
    dotNetControl helpLink "System.Windows.Forms.LinkLabel" pos:[10,490] width:380 height:20
    button btnRun "运行脚本" pos:[10,520] width:120 height:30
    button btnUpdate "检查更新" pos:[140,520] width:120 height:30
    button btnRefresh "刷新列表" pos:[270,520] width:120 height:30
    progressBar updateProgress "更新进度" pos:[10,560] width:380 height:20
    
    -- 全局变量
    local repoOwner = "AniBullet"
    local repoName = "MotionReverse"
    local apiBaseUrl = "https://api.github.com/repos/" + repoOwner + "/" + repoName
    local maxRetries = 3
    local retryDelay = 1000 -- 毫秒
    local scripts = #()
    local tempDir = getDir #temp + "\\MotionReverse\\"
    
    -- 等待函数
    fn wait milliseconds = 
    (
        local start = timeStamp()
        while (timeStamp() - start) < (milliseconds / 1000.0) do
        (
            sleep 0.1
        )
    )
    
    -- 从GitHub获取文件内容
    fn getGitHubFileContent path retryCount:0 = 
    (
        if retryCount >= maxRetries then
        (
            messageBox ("获取文件失败，已达到最大重试次数: " + path) title:"错误"
            return undefined
        )
        
        try
        (
            -- 构建正确的raw.githubusercontent.com URL
            local repoPath = substituteString apiBaseUrl "https://api.github.com/repos/" ""
            local rawUrl = "https://raw.githubusercontent.com/" + repoPath + "/main/" + path
            
            local webClient = dotNetObject "System.Net.WebClient"
            webClient.Headers.Add "User-Agent" "MotionReverse-Tool"
            
            local response = webClient.DownloadString rawUrl
            if response == undefined or response == "" then
            (
                messageBox ("获取文件内容为空: " + rawUrl) title:"错误"
                undefined
            )
            else
            (
                response
            )
        )
        catch
        (
            local errorMsg = getCurrentException()
            if matchPattern errorMsg pattern:"*429*" then
            (
                messageBox "GitHub API访问受限，正在等待重试..." title:"提示"
                wait retryDelay
                getGitHubFileContent path retryCount:(retryCount + 1)
            )
            else if matchPattern errorMsg pattern:"*404*" then
            (
                messageBox ("文件不存在: " + path + "\n完整URL: " + rawUrl) title:"错误"
                undefined
            )
            else
            (
                messageBox ("获取文件失败: " + errorMsg + "\n完整URL: " + rawUrl + "\n正在重试...") title:"错误"
                wait retryDelay
                getGitHubFileContent path retryCount:(retryCount + 1)
            )
        )
    )
    
    -- 从远程获取配置
    fn getRemoteConfig = 
    (
        try
        (
            -- 使用GitHub API获取raw内容
            local url = apiBaseUrl + "/contents/MotionReverse_Config.ini"
            local webClient = dotNetObject "System.Net.WebClient"
            webClient.Headers.Add "User-Agent" "MotionReverse-Tool"
            webClient.Headers.Add "Accept" "application/vnd.github.v3.raw"
            
            local configContent = webClient.DownloadString url
            if configContent == undefined or configContent == "" then
            (
                messageBox "无法获取配置文件内容" title:"错误"
                return false
            )
            
            scripts = #()
            local lines = filterString configContent "\n"
            for line in lines do
            (
                if line != "" and line[1] != ";" and line[1] != "[" then
                (
                    local parts = filterString line "="
                    if parts.count == 2 then
                    (
                        local scriptInfo = filterString parts[2] "|"
                        if scriptInfo.count == 3 then
                        (
                            append scripts #(
                                trimRight (trimLeft parts[1]),
                                trimRight (trimLeft scriptInfo[1]),
                                trimRight (trimLeft scriptInfo[2]),
                                trimRight (trimLeft scriptInfo[3])
                            )
                        )
                    )
                )
            )
            
            if scripts.count == 0 then
            (
                messageBox "配置文件格式错误：没有找到有效的脚本配置" title:"错误"
                return false
            )
            
            true
        )
        catch
        (
            messageBox ("获取配置文件时发生错误: " + (getCurrentException())) title:"错误"
            false
        )
    )
    
    -- 获取远程脚本列表
    fn getRemoteScripts retryCount:0 = 
    (
        if retryCount >= maxRetries then
        (
            messageBox "获取远程脚本列表失败，已达到最大重试次数" title:"错误"
            return #()
        )
        
        local url = apiBaseUrl + "/contents/ms"
        local webClient = dotNetObject "System.Net.WebClient"
        webClient.Headers.Add "User-Agent" "MotionReverse-Tool"
        webClient.Headers.Add "Accept" "application/vnd.github.v3+json"
        
        try
        (
            local response = webClient.DownloadString url
            local scriptFiles = #()
            local lines = filterString response "\n"
            local currentName = undefined
            
            for line in lines do
            (
                if matchPattern line pattern:"*\"name\":*" then
                (
                    local nameStart = findString line "\"name\":"
                    if nameStart != undefined then
                    (
                        local nameStr = substring line (nameStart + 8) -1
                        nameStr = substituteString nameStr "\"" ""
                        nameStr = substituteString nameStr "," ""
                        nameStr = trimRight (trimLeft nameStr)
                        -- 只匹配.ms文件，排除.ini文件
                        if matchPattern nameStr pattern:"*.ms" and not matchPattern nameStr pattern:"*.ini" do
                        (
                            currentName = nameStr
                        )
                    )
                )
                else if matchPattern line pattern:"*\"download_url\":*" and currentName != undefined then
                (
                    local urlStart = findString line "\"download_url\":"
                    if urlStart != undefined then
                    (
                        local urlStr = substring line (urlStart + 15) -1
                        urlStr = substituteString urlStr "\"" ""
                        urlStr = substituteString urlStr "," ""
                        urlStr = trimRight (trimLeft urlStr)
                        append scriptFiles #(currentName, urlStr)
                        currentName = undefined
                    )
                )
            )
            scriptFiles
        )
        catch
        (
            local errorMsg = getCurrentException()
            if matchPattern errorMsg pattern:"*429*" then
            (
                messageBox "GitHub API访问受限，正在等待重试..." title:"提示"
                wait retryDelay
                getRemoteScripts retryCount:(retryCount + 1)
            )
            else if matchPattern errorMsg pattern:"*404*" then
            (
                messageBox "仓库ms目录不存在，请检查仓库结构" title:"错误"
                #()
            )
            else
            (
                messageBox ("获取远程脚本列表失败: " + errorMsg + "\n正在重试...") title:"错误"
                wait retryDelay
                getRemoteScripts retryCount:(retryCount + 1)
            )
        )
    )
    
    -- 下载脚本到临时目录
    fn downloadScriptToTemp scriptName retryCount:0 = 
    (
        if retryCount >= maxRetries then
        (
            messageBox ("下载脚本失败，已达到最大重试次数: " + scriptName) title:"错误"
            return undefined
        )
        
        try
        (
            -- 构建正确的raw.githubusercontent.com URL
            local repoPath = substituteString apiBaseUrl "https://api.github.com/repos/" ""
            local url = "https://raw.githubusercontent.com/" + repoPath + "/main/ms/" + scriptName
            
            local webClient = dotNetObject "System.Net.WebClient"
            webClient.Headers.Add "User-Agent" "MotionReverse-Tool"
            
            local tempFile = getTempFileName()
            webClient.DownloadFile url tempFile
            tempFile
        )
        catch
        (
            local errorMsg = getCurrentException()
            if matchPattern errorMsg pattern:"*429*" then
            (
                messageBox "GitHub API访问受限，正在等待重试..." title:"提示"
                wait retryDelay
                downloadScriptToTemp scriptName retryCount:(retryCount + 1)
            )
            else if matchPattern errorMsg pattern:"*404*" then
            (
                undefined
            )
            else
            (
                messageBox ("下载脚本失败: " + errorMsg + "\n完整URL: " + url + "\n正在重试...") title:"错误"
                wait retryDelay
                downloadScriptToTemp scriptName retryCount:(retryCount + 1)
            )
        )
    )
    
    -- 清理临时文件
    fn cleanupTempFiles = 
    (
        if doesFileExist tempDir then
        (
            local files = getFiles (tempDir + "*.ms")
            for file in files do
            (
                deleteFile file
            )
        )
    )
    
    -- 初始化ListView
    fn initListView = 
    (
        scriptList.View = (dotNetClass "System.Windows.Forms.View").Details
        scriptList.FullRowSelect = true
        scriptList.GridLines = true
        scriptList.Columns.Add "脚本名称" 150
        scriptList.Columns.Add "版本" 80
        scriptList.Columns.Add "状态" 100
    )
    
    -- 更新脚本列表
    fn updateScriptList = 
    (
        scriptList.Items.Clear()
        for script in scripts do
        (
            local item = dotNetObject "System.Windows.Forms.ListViewItem" script[1]
            item.SubItems.Add script[4] -- 版本
            item.SubItems.Add "远程" -- 状态
            scriptList.Items.Add item
        )
    )
    
    -- 运行选中的脚本
    fn runSelectedScript = 
    (
        if scriptList.SelectedItems.Count > 0 then
        (
            local scriptName = scriptList.SelectedItems.Item[0].Text
            local scriptFile = downloadScriptToTemp (scriptName + ".ms")
            
            if scriptFile != undefined then
            (
                try
                (
                    fileIn scriptFile
                    cleanupTempFiles()
                )
                catch
                (
                    messageBox ("运行脚本时出错: " + (getCurrentException())) title:"错误"
                )
            )
            else
            (
                messageBox ("无法获取脚本文件: " + scriptName + ".ms") title:"错误"
            )
        )
    )
    
    -- 检查更新
    fn checkForUpdates = 
    (
        updateProgress.visible = true
        updateProgress.value = 0
        
        if getRemoteConfig() then
        (
            updateProgress.value = 100
            updateScriptList()
            messageBox "配置已更新！" title:"提示"
        )
        else
        (
            messageBox "更新配置失败！" title:"错误"
        )
        
        updateProgress.visible = false
    )
    
    -- 事件处理
    on MotionReverseTool open do
    (
        initListView()
        if getRemoteConfig() then
        (
            updateScriptList()
        )
        else
        (
            messageBox "无法获取远程配置！" title:"错误"
        )
        updateProgress.visible = false
    )
    
    on MotionReverseTool close do
    (
        cleanupTempFiles()
    )
    
    on scriptList SelectedIndexChanged args do
    (
        if scriptList.SelectedItems.Count > 0 then
        (
            local scriptName = scriptList.SelectedItems.Item[0].Text
            for script in scripts do
            (
                if script[1] == scriptName then
                (
                    descriptionBox.Text = script[2]
                    helpLink.Text = "查看帮助文档"
                    helpLink.Links.Clear()
                    local linkText = "查看帮助文档"
                    helpLink.Links.Add 0 (linkText.count) script[3]
                    exit
                )
            )
        )
    )
    
    on helpLink LinkClicked args do
    (
        if scriptList.SelectedItems.Count > 0 then
        (
            local scriptName = scriptList.SelectedItems.Item[0].Text
            for script in scripts do
            (
                if script[1] == scriptName then
                (
                    shellLaunch "explorer.exe" script[3]
                    exit
                )
            )
        )
    )
    
    on btnRun pressed do runSelectedScript()
    on btnUpdate pressed do checkForUpdates()
    on btnRefresh pressed do 
    (
        if getRemoteConfig() then
        (
            updateScriptList()
        )
        else
        (
            messageBox "刷新配置失败！" title:"错误"
        )
    )
)

-- 创建工具窗口
createDialog MotionReverseTool 