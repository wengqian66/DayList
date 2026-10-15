$ErrorActionPreference = "Continue"
[System.Globalization.CultureInfo]::CurrentUICulture = [System.Globalization.CultureInfo]::GetCultureInfo("en-US")
[System.Globalization.CultureInfo]::DefaultThreadCurrentUICulture = [System.Globalization.CultureInfo]::GetCultureInfo("en-US")
[System.Globalization.CultureInfo]::DefaultThreadCurrentCulture = [System.Globalization.CultureInfo]::GetCultureInfo("en-US")
$env:DOTNET_CLI_UI_LANGUAGE = "en"
Set-Location "D:\wengqian\DayList"
dotnet build src/DayList.App -c Release --nologo