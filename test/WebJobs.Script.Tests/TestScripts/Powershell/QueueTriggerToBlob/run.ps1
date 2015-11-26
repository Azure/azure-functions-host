$in = [System.Console]::ReadLine()

[System.Console]::WriteLine("Powershell script processed queue message: '$in'")

$output = (Get-Item Env:output).Value
$in | Out-File $output