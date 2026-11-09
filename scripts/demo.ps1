#requires -Version 7.0
param(
    [uri]$BaseUri = 'http://127.0.0.1:8080',
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
if ($OutputPath -and (Test-Path -LiteralPath $OutputPath)) {
    throw 'Evidence output already exists; choose a new filename.'
}
if (-not $BaseUri.IsLoopback -or $BaseUri.Scheme -notin @('http', 'https')) {
    throw 'Run this synthetic-data walkthrough against a loopback demo instance only.'
}
if ([string]::IsNullOrWhiteSpace($env:INVENTORY_WRITE_KEY)) {
    throw 'Set INVENTORY_WRITE_KEY to the local API write key without committing it.'
}
$base = $BaseUri.AbsoluteUri.TrimEnd('/')
$headers = @{ 'X-Inventory-Key' = $env:INVENTORY_WRITE_KEY }
$observations = [Collections.Generic.List[object]]::new()

function Send-Request([string]$Method, [string]$Route, $Body, [int]$Expected) {
    $arguments = @{ Uri = "$base$Route"; Method = $Method; Headers = $headers; SkipHttpErrorCheck = $true }
    if ($null -ne $Body) {
        $arguments.ContentType = 'application/json'
        $arguments.Body = $Body | ConvertTo-Json -Depth 6 -Compress
    }
    $response = Invoke-WebRequest @arguments
    if ([int]$response.StatusCode -ne $Expected) {
        throw "$Method $Route returned $($response.StatusCode); expected $Expected."
    }
    if ($response.Content) { return $response.Content | ConvertFrom-Json }
}

function Assert-Stock([string]$Step, [int]$OnHand, [int]$Reserved, [int]$Status) {
    $listing = Send-Request 'GET' "/products?categoryId=$($category.id)" $null 200
    $productState = @($listing.items | Where-Object id -eq $product.id)
    if ($productState.Count -ne 1 -or $productState[0].onHand -ne $OnHand -or
        $productState[0].reserved -ne $Reserved -or $productState[0].available -ne ($OnHand - $Reserved)) {
        throw "Unexpected persisted stock after $Step."
    }
    $observations.Add([ordered]@{ step = $Step; httpStatus = $Status; onHand = $OnHand; reserved = $Reserved; available = $OnHand - $Reserved })
}

$suffix = [Guid]::NewGuid().ToString('N')
$category = Send-Request 'POST' '/categories' @{ name = "Portfolio-$suffix" } 201
$product = Send-Request 'POST' '/products' @{ categoryId = $category.id; sku = "DEMO-$suffix"; name = 'Synthetic demo item'; price = 10.25; onHand = 5 } 201
Assert-Stock 'Initial stock' 5 0 201
$four = @{ items = @(@{ productId = $product.id; quantity = 4 }) }
$first = Send-Request 'POST' '/orders' $four 201
Assert-Stock 'Reserve four' 5 4 201
$null = Send-Request 'POST' '/orders' $four 409
Assert-Stock 'Reject insufficient stock' 5 4 409
$null = Send-Request 'POST' "/orders/$($first.id)/cancel" $null 204
Assert-Stock 'Cancel first order' 5 0 204
$second = Send-Request 'POST' '/orders' @{ items = @(@{ productId = $product.id; quantity = 2 }) } 201
Assert-Stock 'Reserve two' 5 2 201
$null = Send-Request 'POST' "/orders/$($second.id)/fulfill" $null 204
Assert-Stock 'Fulfill second order' 3 0 204

$observations | ForEach-Object { [pscustomobject]$_ } | Format-Table
if ($OutputPath) {
    [ordered]@{ capturedAtUtc = [DateTime]::UtcNow.ToString('o'); steps = $observations.ToArray() } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding utf8
}
