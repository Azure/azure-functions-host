Function Get-DateToday
{
    Get-Date -DisplayHint date;
	Write-Error "Test Error in Get-DateToday"
}
Export-ModuleMember -function Get-DateToday