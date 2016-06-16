$in = Get-Content $order

$order = $in | ConvertFrom-Json
$content = [string]::Format('{{ "Subject": "Thanks for your order (#{0})", "Text": "{1}, your order ({0}) is being processed!" }}', $order.OrderId, $order.CustomerName)

$content | Out-File -Encoding Ascii $message