param([string]$Pdf, [int]$From = 1, [int]$To = 0, [string]$OutDir, [switch]$CountOnly, [switch]$Png)
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$m = [System.WindowsRuntimeSystemExtensions].GetMethods()
$asTask = ($m | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
$asAction = ($m | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' })[0]
function Await($op, $t) { $task = $asTask.MakeGenericMethod($t).Invoke($null, @($op)); $task.Wait(); $task.Result }
[Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime] | Out-Null
[Windows.Data.Pdf.PdfDocument,Windows.Data.Pdf,ContentType=WindowsRuntime] | Out-Null
[Windows.Storage.Streams.InMemoryRandomAccessStream,Windows.Storage.Streams,ContentType=WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.BitmapDecoder,Windows.Graphics.Imaging,ContentType=WindowsRuntime] | Out-Null
[Windows.Media.Ocr.OcrEngine,Windows.Foundation,ContentType=WindowsRuntime] | Out-Null

$file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($Pdf)) ([Windows.Storage.StorageFile])
$doc = Await ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)) ([Windows.Data.Pdf.PdfDocument])
if ($CountOnly) { "Pages: $($doc.PageCount)"; return }
if ($To -le 0 -or $To -gt $doc.PageCount) { $To = $doc.PageCount }
[IO.Directory]::CreateDirectory($OutDir) | Out-Null
$ocr = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()

for ($p = $From; $p -le $To; $p++) {
  $page = $doc.GetPage($p - 1)
  $opts = New-Object Windows.Data.Pdf.PdfPageRenderOptions
  $opts.DestinationWidth = 1800
  $s = New-Object Windows.Storage.Streams.InMemoryRandomAccessStream
  $t = $asAction.Invoke($null, @($page.RenderToStreamAsync($s, $opts))); $t.Wait()
  if ($Png) {
    $ns = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($s.GetInputStreamAt(0))
    $fs = [IO.File]::Create((Join-Path $OutDir ("p{0:D4}.png" -f $p))); $ns.CopyTo($fs); $fs.Close()
  }
  $s.Seek(0)
  $dec = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($s)) ([Windows.Graphics.Imaging.BitmapDecoder])
  $bmp = Await ($dec.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
  $res = Await ($ocr.RecognizeAsync($bmp)) ([Windows.Media.Ocr.OcrResult])
  $lines = $res.Lines | ForEach-Object { $_.Text }
  [IO.File]::WriteAllText((Join-Path $OutDir ("p{0:D4}.txt" -f $p)), ($lines -join "`n"))
  $bmp.Dispose(); $s.Dispose()
}
"Done $From-$To"
