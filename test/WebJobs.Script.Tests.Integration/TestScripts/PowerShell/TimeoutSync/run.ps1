$in = Get-Content $inputData

"{0}" -f $in

$count = 0
while ($count -lt 10)
{    
    $count = $count + 1
    Start-Sleep -s 1
}