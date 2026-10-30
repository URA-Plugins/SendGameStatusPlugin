#!/usr/bin/env pwsh
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PluginRoot)

$ErrorActionPreference = 'Stop'
$pluginRoot = [IO.Path]::GetFullPath($PluginRoot)
$item = [pscustomobject]@{ name = 'SendGameStatusPlugin'; project = 'SendGameStatusPlugin.csproj' }
$projectPath = Join-Path $pluginRoot $item.project
function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

foreach ($removedFile in @('Directory.Build.props', 'Directory.Build.targets')) {
    Assert (-not (Test-Path -LiteralPath (Join-Path $pluginRoot $removedFile))) "$($item.name) 仍包含旧 Host build import：$removedFile。"
}

$nugetConfigPath = Join-Path $pluginRoot 'NuGet.Config'
Assert (Test-Path -LiteralPath $nugetConfigPath -PathType Leaf) "$($item.name) 缺少 NuGet.Config。"
[xml]$nugetConfig = Get-Content -LiteralPath $nugetConfigPath -Raw
Assert ($null -ne $nugetConfig.SelectSingleNode('/configuration/packageSources/add[@key="ura.shuise.net" and @value="https://ura.shuise.net/nuget/index.json"]')) "$($item.name) 未配置 URA NuGet 源。"
Assert ($null -ne $nugetConfig.SelectSingleNode('/configuration/packageSourceMapping/packageSource[@key="ura.shuise.net"]/package[@pattern="UmamusumeResponseAnalyzer"]')) "$($item.name) 未将 Host 包精确映射到 URA NuGet 源。"
Assert ($null -ne $nugetConfig.SelectSingleNode('/configuration/packageSourceMapping/packageSource[@key="nuget.org"]/package[@pattern="*"]')) "$($item.name) 未将其它包映射到 nuget.org。"

$nestedModulePaths = @()
$nestedModuleUrls = @()
$nestedModulesFile = Join-Path $pluginRoot '.gitmodules'
if (Test-Path -LiteralPath $nestedModulesFile -PathType Leaf) {
    $nestedModulePaths = @(git -C $pluginRoot config -f .gitmodules --get-regexp '^submodule\..*\.path$' | ForEach-Object { ($_ -split '\s+', 2)[1] })
    Assert ($LASTEXITCODE -eq 0) "$($item.name) 无法读取 .gitmodules path。"
    $nestedModuleUrls = @(git -C $pluginRoot config -f .gitmodules --get-regexp '^submodule\..*\.url$' | ForEach-Object { ($_ -split '\s+', 2)[1] })
    Assert ($LASTEXITCODE -eq 0) "$($item.name) 无法读取 .gitmodules url。"
}
Assert ('deps/UmamusumeResponseAnalyzer' -notin $nestedModulePaths) "$($item.name) 仍声明 Host submodule。"
$trackedHostPaths = @(git -C $pluginRoot ls-tree -r --name-only HEAD -- 'deps/UmamusumeResponseAnalyzer')
Assert ($LASTEXITCODE -eq 0 -and $trackedHostPaths.Count -eq 0) "$($item.name) 的提交树仍包含 Host checkout。"
Assert ($nestedModuleUrls.Count -eq $nestedModulePaths.Count -and @($nestedModuleUrls | Where-Object { $_ -notmatch '^https://github\.com/' }).Count -eq 0) "$($item.name) 的源码依赖必须全部使用公开 GitHub URL。"
foreach ($nestedModulePath in $nestedModulePaths) {
    $nestedEntry = git -C $pluginRoot ls-files --stage -- $nestedModulePath
    Assert ($LASTEXITCODE -eq 0 -and $nestedEntry -match '^160000 [0-9a-f]{40} 0\s+') "$($item.name)/$nestedModulePath 不是固定 gitlink。"
    $nestedExpectedCommit = ($nestedEntry -split '\s+')[1]
    $nestedActualCommit = git -C (Join-Path $pluginRoot $nestedModulePath) rev-parse HEAD
    Assert ($LASTEXITCODE -eq 0 -and $nestedActualCommit -eq $nestedExpectedCommit) "$($item.name)/$nestedModulePath checkout 未处于固定 commit $nestedExpectedCommit。"
}

[xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw
Assert ($null -ne $projectXml.SelectSingleNode('//IsUraPlugin[text()="true"]')) "$($item.name) 未声明 IsUraPlugin=true。"
$hostPackageReferences = @($projectXml.SelectNodes('//PackageReference[@Include="UmamusumeResponseAnalyzer"]'))
Assert ($hostPackageReferences.Count -eq 1 -and $hostPackageReferences[0].Version -eq '*' -and $hostPackageReferences[0].PrivateAssets -eq 'all') "$($item.name) 必须且只能引用一次 UmamusumeResponseAnalyzer，并使用 Version=*、PrivateAssets=all。"
foreach ($reference in $projectXml.SelectNodes('//ProjectReference')) {
    $include = [string]$reference.Include
    Assert (-not $include.Contains('$(')) "$($item.name) 使用了未审计的 ProjectReference 属性：$include"
    $resolved = [IO.Path]::GetFullPath((Join-Path (Split-Path $projectPath -Parent) $include))
    Assert ($resolved.StartsWith("$pluginRoot\", [StringComparison]::OrdinalIgnoreCase)) "$($item.name) 的 ProjectReference 越出仓库：$include"
    Assert (Test-Path -LiteralPath $resolved -PathType Leaf) "$($item.name) 的 ProjectReference 不存在：$include"
}

Write-Host "发布边界验证通过：$($item.name)。" -ForegroundColor Green
