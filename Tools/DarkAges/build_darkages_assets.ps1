# Cuts the character creation popup's inner pieces out of the user's "Dark Ages UI v1.0" tilesheet (Hypnobius) into
# AlloyClient/AlloyClient/Content/Ui/DarkAges. (The popup's wooden main frame comes from build_wood_frame_assets.ps1.)
#
# The pack's boxes are near-black / charcoal / grey, which fight the wood frame, so the two box pieces (Panel, Slot) are
# recoloured onto a walnut-to-tan ramp: every pixel keeps its brightness (so the bevels, borders and inner rims stay exactly
# as drawn) but gets a brown hue. The parchment and divider are already tan and are cut untouched.
#
# Run from the repo root:   powershell -File Tools/DarkAges/build_darkages_assets.ps1
# Then keep the DarkAges entries in Content/Ui.atlas and rebuild AlloyTk.sln.
#
# NOTE - the pack's licence forbids redistributing it "either modified or in its original form"; these crops are
# derived from it (see LICENSE.txt in the pack).

param(
    [string]$Sheet = "$env:USERPROFILE\Desktop\Assets and GUI\DarkAgesUi_v1.0\32x32-Tilesheet.png",
    [string]$Out = "AlloyClient/AlloyClient/Content/Ui/DarkAges"
)

Add-Type -AssemblyName System.Drawing

# brightness (0..1) -> colour stops for the brown ramp: (position, R, G, B)
$Ramp = @(
    @(0.00, 22, 13, 8),
    @(0.15, 58, 38, 24),
    @(0.35, 100, 68, 42),
    @(0.55, 156, 114, 70),
    @(0.75, 200, 160, 106),
    @(1.00, 236, 208, 156)
)

function Get-Brown([double]$l) {
    for ($i = 1; $i -lt $Ramp.Count; $i++) {
        if ($l -le $Ramp[$i][0]) {
            $a = $Ramp[$i - 1]; $b = $Ramp[$i]
            $t = ($l - $a[0]) / ($b[0] - $a[0])
            return @(
                [int][Math]::Round($a[1] + ($b[1] - $a[1]) * $t),
                [int][Math]::Round($a[2] + ($b[2] - $a[2]) * $t),
                [int][Math]::Round($a[3] + ($b[3] - $a[3]) * $t)
            )
        }
    }
    return @($Ramp[-1][1], $Ramp[-1][2], $Ramp[-1][3])
}

$src = New-Object System.Drawing.Bitmap $Sheet
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path

function Save-Crop([string]$name, [int]$sx, [int]$sy, [int]$w, [int]$h, [switch]$Brown) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($y = 0; $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            $p = $src.GetPixel($sx + $x, $sy + $y)
            if ($Brown -and $p.A -gt 0) {
                $l = (0.299 * $p.R + 0.587 * $p.G + 0.114 * $p.B) / 255.0
                $c = Get-Brown $l
                $p = [System.Drawing.Color]::FromArgb($p.A, $c[0], $c[1], $c[2])
            }
            $bmp.SetPixel($x, $y, $p)
        }
    }
    $bmp.Save("$Out\$name.png", [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "wrote $name.png (${w}x${h})$(if ($Brown) { ' - recoloured brown' })"
}

# ---- pieces (sheet pixel bounds measured from the sheet's own alpha)
Save-Crop 'Panel'     194 101 28 27 -Brown   # dark rounded box - the popup's two big cards and its CANCEL / BEGIN buttons
Save-Crop 'Slot'      226 101 28 27 -Brown   # the same box, a shade lighter - class tiles and gear slots
Save-Crop 'Parchment' 0   319 64 33          # torn parchment strip - the class description plate

$src.Dispose()
