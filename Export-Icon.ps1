param(
    [Parameter(Mandatory=$true)][string]$InputPath,
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$iconInput = (Resolve-Path -LiteralPath $InputPath).Path
$iconOutput = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $iconOutput) { throw '目标图标已存在，请使用新版本文件名。' }
$iconSizes = @(16,24,32,48,64,128,256)
$iconFrames = [Collections.Generic.List[byte[]]]::new()
$iconSource = [Drawing.Bitmap]::new($iconInput)
try {
    foreach ($iconSize in $iconSizes) {
        $iconBitmap = [Drawing.Bitmap]::new($iconSize, $iconSize, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $iconGraphics = [Drawing.Graphics]::FromImage($iconBitmap)
        $iconPngStream = [IO.MemoryStream]::new()
        $iconAttributes = [Drawing.Imaging.ImageAttributes]::new()
        try {
            $iconGraphics.Clear([Drawing.Color]::Transparent)
            $iconGraphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $iconGraphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
            $iconGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $iconGraphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $iconAttributes.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
            $iconRectangle = [Drawing.Rectangle]::new(0,0,$iconSize,$iconSize)
            $iconGraphics.DrawImage($iconSource, $iconRectangle, 0, 0, $iconSource.Width, $iconSource.Height, [Drawing.GraphicsUnit]::Pixel, $iconAttributes)
            $iconBitmap.Save($iconPngStream, [Drawing.Imaging.ImageFormat]::Png)
            $iconFrames.Add($iconPngStream.ToArray())
        } finally {
            $iconAttributes.Dispose()
            $iconGraphics.Dispose()
            $iconBitmap.Dispose()
            $iconPngStream.Dispose()
        }
    }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($iconOutput)) | Out-Null
    $iconFile = [IO.File]::Open($iconOutput, [IO.FileMode]::CreateNew)
    $iconWriter = [IO.BinaryWriter]::new($iconFile)
    try {
        $iconWriter.Write([uint16]0)
        $iconWriter.Write([uint16]1)
        $iconWriter.Write([uint16]$iconSizes.Count)
        $iconOffset = 6 + 16 * $iconSizes.Count
        for ($iconIndex = 0; $iconIndex -lt $iconSizes.Count; $iconIndex++) {
            $iconDimension = if ($iconSizes[$iconIndex] -eq 256) { 0 } else { $iconSizes[$iconIndex] }
            $iconWriter.Write([byte]$iconDimension)
            $iconWriter.Write([byte]$iconDimension)
            $iconWriter.Write([byte]0)
            $iconWriter.Write([byte]0)
            $iconWriter.Write([uint16]1)
            $iconWriter.Write([uint16]32)
            $iconWriter.Write([uint32]$iconFrames[$iconIndex].Length)
            $iconWriter.Write([uint32]$iconOffset)
            $iconOffset += $iconFrames[$iconIndex].Length
        }
        foreach ($iconFrame in $iconFrames) { $iconWriter.Write([byte[]]$iconFrame) }
    } finally { $iconWriter.Dispose(); $iconFile.Dispose() }
    [pscustomobject]@{Icon=$iconOutput;Sizes=$iconSizes;SourceWidth=$iconSource.Width;SourceHeight=$iconSource.Height;CornerAlpha=$iconSource.GetPixel(0,0).A} | ConvertTo-Json -Compress
} finally { $iconSource.Dispose() }
