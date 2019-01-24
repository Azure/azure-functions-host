param($req)

Push-OutputBinding -Name res -Value @{
    StatusCode = 200
    Body = "Pong"
}
