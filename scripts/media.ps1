param([string]$cmd = 'status')
# Reads / controls the Windows "System Media Transport Controls" session (Spotify, browsers, etc.)
$ErrorActionPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
  $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
function Await($op, [Type]$t) {
  $task = $asTask.MakeGenericMethod($t).Invoke($null, @($op)); $task.Wait(-1) | Out-Null; $task.Result
}
[void][Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType = WindowsRuntime]
[void][Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties, Windows.Media.Control, ContentType = WindowsRuntime]
[void][Windows.Storage.Streams.IRandomAccessStreamWithContentType, Windows.Storage.Streams, ContentType = WindowsRuntime]
$mgr = Await ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager])
$s = $mgr.GetCurrentSession()
if (-not $s) { Write-Output '{"active":false}'; exit }
switch ($cmd) {
  'play' { Await ($s.TryTogglePlayPauseAsync()) ([bool]) | Out-Null }
  'next' { Await ($s.TrySkipNextAsync()) ([bool]) | Out-Null }
  'prev' { Await ($s.TrySkipPreviousAsync()) ([bool]) | Out-Null }
}
$p = Await ($s.TryGetMediaPropertiesAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties])
$info = $s.GetPlaybackInfo(); $tl = $s.GetTimelineProperties()
$o = [ordered]@{
  active  = $true
  title   = [string]$p.Title
  artist  = [string]$p.Artist
  app     = [string]$s.SourceAppUserModelId
  playing = ([int]$info.PlaybackStatus -eq 4)
  pos     = [math]::Round($tl.Position.TotalSeconds, 1)
  dur     = [math]::Round($tl.EndTime.TotalSeconds, 1)
}
if ($cmd -eq 'thumb' -and $p.Thumbnail) {
  $rs = Await ($p.Thumbnail.OpenReadAsync()) ([Windows.Storage.Streams.IRandomAccessStreamWithContentType])
  $net = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($rs)
  $ms = New-Object System.IO.MemoryStream; $net.CopyTo($ms)
  $o.thumb = 'data:' + $rs.ContentType + ';base64,' + [Convert]::ToBase64String($ms.ToArray())
}
$o | ConvertTo-Json -Compress
