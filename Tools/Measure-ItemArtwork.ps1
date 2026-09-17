# Read-only pixel inspection: record transparent row gutters as Sprite rect metadata.
# Never changes the generated PNGs.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$atlasRoot = Join-Path $PSScriptRoot '../Assets/Resources/ItemIcons'
$manifestPath = Join-Path $atlasRoot 'catalog.json'
$catalog = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach ($sheet in ($catalog.entries | Group-Object resource)) {
    $bitmap = [System.Drawing.Bitmap]::new((Join-Path $atlasRoot (($sheet.Name.Split('/')[-1]) + '.png')))
    try {
        foreach ($column in ($sheet.Group | Group-Object column)) {
            $left = [int]([int]$column.Name * $bitmap.Width / $catalog.columns)
            $right = [int](([int]$column.Name + 1) * $bitmap.Width / $catalog.columns)
            $edges = @(0)
            for ($row = 1; $row -lt $catalog.rows; $row++) {
                $nominal = [int]($row * $bitmap.Height / $catalog.rows)
                $best = $nominal; $bestScore = [int]::MaxValue
                for ($y = $nominal - 42; $y -le $nominal + 42; $y++) {
                    $count = 0
                    for ($x = $left; $x -lt $right; $x++) {
                        if ($bitmap.GetPixel($x, $y).A -gt 32) { $count++ }
                    }
                    $score = $count * 100 + [Math]::Abs($y - $nominal)
                    if ($score -lt $bestScore) { $bestScore = $score; $best = $y }
                }
                $edges += $best
            }
            $edges += $bitmap.Height
            foreach ($entry in $column.Group) {
                $entry | Add-Member -Force NoteProperty top $edges[$entry.row]
                $entry | Add-Member -Force NoteProperty bottom $edges[$entry.row + 1]
            }
        }
    } finally { $bitmap.Dispose() }
}
$catalog | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Output "Measured transparent gutters for $($catalog.entries.Count) item rectangles."
