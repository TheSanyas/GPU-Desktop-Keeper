$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$keeperIconPath = Join-Path $PSScriptRoot 'Keeper.ico'
$keeperFrames = @()
foreach ($keeperSize in @(16,32,48,64,256)) {
    $keeperBitmap = [Drawing.Bitmap]::new($keeperSize,$keeperSize)
    $keeperGraphics = [Drawing.Graphics]::FromImage($keeperBitmap)
    $keeperPen = [Drawing.Pen]::new([Drawing.Color]::White,2.0)
    $keeperBlue = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(35,103,204))
    $keeperGreen = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(62,217,150))
    $keeperMemory = [IO.MemoryStream]::new()
    try {
        $keeperGraphics.Clear([Drawing.Color]::Transparent)
        $keeperGraphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $keeperGraphics.ScaleTransform(($keeperSize/32.0),($keeperSize/32.0))
        $keeperGraphics.FillEllipse($keeperBlue,1,1,30,30)
        $keeperGraphics.DrawRectangle($keeperPen,7,8,18,13)
        $keeperGraphics.DrawLine($keeperPen,16,21,16,25)
        $keeperGraphics.DrawLine($keeperPen,11,25,21,25)
        $keeperGraphics.FillEllipse($keeperGreen,21,4,8,8)
        $keeperBitmap.Save($keeperMemory,[Drawing.Imaging.ImageFormat]::Png)
        $keeperFrames += ,@{Size=$keeperSize; Bytes=$keeperMemory.ToArray()}
    } finally { $keeperMemory.Dispose(); $keeperGreen.Dispose(); $keeperBlue.Dispose(); $keeperPen.Dispose(); $keeperGraphics.Dispose(); $keeperBitmap.Dispose() }
}
$keeperFile = [IO.File]::Create($keeperIconPath)
$keeperWriter = [IO.BinaryWriter]::new($keeperFile)
try {
    $keeperWriter.Write([uint16]0); $keeperWriter.Write([uint16]1); $keeperWriter.Write([uint16]$keeperFrames.Count)
    $keeperOffset = 6+16*$keeperFrames.Count
    foreach ($keeperFrame in $keeperFrames) {
        $keeperDimension = if ($keeperFrame.Size -eq 256) { 0 } else { $keeperFrame.Size }
        $keeperWriter.Write([byte]$keeperDimension); $keeperWriter.Write([byte]$keeperDimension)
        $keeperWriter.Write([byte]0); $keeperWriter.Write([byte]0)
        $keeperWriter.Write([uint16]1); $keeperWriter.Write([uint16]32)
        $keeperWriter.Write([uint32]$keeperFrame.Bytes.Length); $keeperWriter.Write([uint32]$keeperOffset)
        $keeperOffset += $keeperFrame.Bytes.Length
    }
    foreach ($keeperFrame in $keeperFrames) { $keeperWriter.Write([byte[]]$keeperFrame.Bytes) }
} finally { $keeperWriter.Dispose() }
