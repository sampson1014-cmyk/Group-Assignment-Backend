param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [Parameter(Mandatory = $true)][string]$Email,
    [Parameter(Mandatory = $true)][string]$Password
)

$ErrorActionPreference = 'Stop'
$base = $BaseUrl.TrimEnd('/')
$loginPage = Invoke-WebRequest -Uri "$base/Account/Login" -SessionVariable memberSession -UseBasicParsing
$tokenMatch = [regex]::Match($loginPage.Content, 'name="__RequestVerificationToken" type="hidden" value="([^"]+)"')
if (-not $tokenMatch.Success) { throw 'The login anti-forgery token was not found.' }

$loginResponse = Invoke-WebRequest -Method Post -Uri "$base/Account/Login" -WebSession $memberSession -UseBasicParsing -Body @{
    Identifier = $Email
    Password = $Password
    RememberMe = 'false'
    __RequestVerificationToken = $tokenMatch.Groups[1].Value
}

if ($loginResponse.BaseResponse.RequestMessage.RequestUri.AbsolutePath -ne '/Member/Dashboard')
{
    throw 'Member login did not reach the dashboard.'
}

$paths = @(
    '/Member/Dashboard', '/Member/Wallet', '/Member/Rewards', '/Member/Stamps',
    '/Member/History', '/Member/Notifications', '/Member/Referrals', '/Member/Outlets',
    '/Member/Profile', '/Member/EditProfile', '/Member/Feedback', '/Member/VerifyEmail',
    '/Member/MemberQr', '/Member/BalanceSnapshot', '/health'
)

$paths += @(
    '/Member/History?type=Wallet', '/Member/History?type=Top-up',
    '/Member/History?type=Points', '/Member/History?type=Stamp',
    '/Member/History?type=Reward', '/Member/History?type=Voucher',
    '/Member/History?type=Stamps%20used', '/Member/History?type=Spending',
    '/Member/History?type=Voucher%20activity'
)

foreach ($path in $paths)
{
    $response = Invoke-WebRequest -Uri "$base$path" -WebSession $memberSession -UseBasicParsing -SkipHttpErrorCheck
    if ($response.StatusCode -ne 200) { throw "$path returned HTTP $($response.StatusCode)." }
    [pscustomobject]@{ Path = $path; Status = $response.StatusCode; ContentType = $response.Headers.'Content-Type' }
}
