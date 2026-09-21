# Rewrites lang\INIMaster.template.txt from the strings in the source: every
# Loc.T and Loc.Plural call in C# and every {l:T '...'} in XAML. The test
# LocalizationTests.TemplateListsEveryString fails until this has been run
# after a string changes.
$ErrorActionPreference = 'Stop'
$env:INIMASTER_WRITE_TEMPLATE = '1'
try {
    dotnet test (Join-Path $PSScriptRoot '..\tests\IniMaster.Tests') -nologo --filter 'FullyQualifiedName~LocalizationTests.TemplateListsEveryString'
    if ($LASTEXITCODE) { throw 'writing the template failed' }
}
finally { Remove-Item Env:INIMASTER_WRITE_TEMPLATE }
Get-Content (Join-Path $PSScriptRoot '..\lang\INIMaster.template.txt') | Where-Object { $_ -and -not $_.StartsWith('#') } | Measure-Object | ForEach-Object { "$($_.Count) strings in lang\INIMaster.template.txt" }
