#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$srcDoc = Join-Path $repoRoot 'docs\pz\template-inprogress.docx'
$outDoc = Join-Path $repoRoot 'docs\pz\MiniFinance-PZ.docx'
$shotDir = Join-Path $repoRoot 'docs\pz\screenshots'
$listingPath = Join-Path $repoRoot 'Program.cs'
$contentPath = Join-Path $repoRoot 'docs\pz\pz-content.json'

$content = Get-Content -LiteralPath $contentPath -Raw -Encoding UTF8 | ConvertFrom-Json

if (-not (Test-Path -LiteralPath $srcDoc)) { throw "Source doc not found" }
New-Item -ItemType Directory -Force -Path (Split-Path $outDoc) | Out-Null
Copy-Item -LiteralPath $srcDoc -Destination $outDoc -Force

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
$doc = $word.Documents.Open($outDoc)

function Set-BodyFormat($range) {
    $range.Font.Name = 'Times New Roman'
    $range.Font.Size = 14
    $range.ParagraphFormat.LineSpacingRule = 4
    $range.ParagraphFormat.LineSpacing = 18
    $range.ParagraphFormat.FirstLineIndent = 35.45
    $range.ParagraphFormat.Alignment = 3
}

function Add-Para($text) {
    $r = $doc.Content
    $r.Collapse(0)
    $r.InsertAfter("$text`r")
    Set-BodyFormat $doc.Paragraphs.Item($doc.Paragraphs.Count).Range
}

function Add-Heading($text) {
    $r = $doc.Content
    $r.Collapse(0)
    $r.InsertAfter("`r$text`r")
    $p = $doc.Paragraphs.Item($doc.Paragraphs.Count)
    $p.Range.Font.Name = 'Times New Roman'
    $p.Range.Font.Size = 16
    $p.Range.Font.Bold = $true
    $p.Range.ParagraphFormat.Alignment = 3
    $p.Range.ParagraphFormat.FirstLineIndent = 35.45
}

function Add-Image($file, $caption) {
    $path = Join-Path $shotDir $file
    if (-not (Test-Path $path)) { return }
    $r = $doc.Content
    $r.Collapse(0)
    $r.InsertParagraphAfter() | Out-Null
    $sel = $word.Selection
    $sel.EndKey(6) | Out-Null
    $pic = $sel.InlineShapes.AddPicture($path)
    $pic.LockAspectRatio = $true
    $pic.Width = 397
    $sel.TypeParagraph()
    $sel.ParagraphFormat.Alignment = 1
    $sel.ParagraphFormat.FirstLineIndent = 0
    $sel.Font.Name = 'Times New Roman'
    $sel.Font.Size = 14
    $sel.TypeText($caption)
    $sel.TypeParagraph()
}

function Replace-All($find, $replace) {
    $f = $doc.Content.Find
    $f.ClearFormatting()
    $f.Replacement.ClearFormatting()
    $f.Text = $find
    $f.Replacement.Text = $replace
    $f.Forward = $true
    $f.Wrap = 1
    [void]$f.Execute($replace, 2)
}

Replace-All 'FINANCE CONTROL' 'MiniFinance'
Replace-All '.NET 8' '.NET 9'
Replace-All 'на 99 листах' 'на 48 листах'
Replace-All 'C++ Builder' 'Visual Studio / .NET 9'
Replace-All 'используется C++.' 'используется C#.'

$testHeader = -join ([char[]](0x0422,0x0415,0x0421,0x0422,0x0418,0x0420,0x041E,0x0412,0x0410,0x041D,0x0418,0x0415))
$startPos = $null
$endPos = $null
foreach ($p in @($doc.Paragraphs)) {
    $t = $p.Range.Text
    if ($t -like '*9 *' -and $t -like '*:*') { $startPos = $p.Range.Start }
    if ($t -like "*$testHeader*" -and $startPos) { $endPos = $p.Range.Start; break }
}
if ($startPos -and $endPos) { $doc.Range($startPos, $endPos).Delete() }

$idx = 0
foreach ($img in $content.images) {
    if ($idx -lt $content.paragraphs.Count) { Add-Para $content.paragraphs[$idx] }
    Add-Image $img.file $img.caption
    $idx++
}

Add-Heading $content.headings[0]
Add-Para $content.logic
Add-Heading $content.headings[1]
Add-Para $content.physical

foreach ($prop in $content.inlineImages.PSObject.Properties) {
    $find = $doc.Content.Find
    $find.Text = $prop.Name
    $find.Forward = $true
    if ($find.Execute()) {
        $sel = $word.Selection
        $sel.Collapse(0)
        $sel.InsertParagraphAfter() | Out-Null
        $sel.EndKey(6) | Out-Null
        $path = Join-Path $shotDir $prop.Value
        if (Test-Path $path) {
            $pic = $sel.InlineShapes.AddPicture($path)
            $pic.Width = 397
            $sel.TypeParagraph()
        }
    }
}

Replace-All 'C++: базовый курс' 'C# and .NET'
Replace-All 'Язык программирования C++' 'ASP.NET Core Blazor'
Replace-All 'Booksee.org' 'Microsoft Learn'
Replace-All 'Helloworld.ru' 'Entity Framework Core'
Replace-All 'Medium.com' 'SQLite.org'

foreach ($p in @($doc.Paragraphs)) {
    if ($p.Range.Text -like '*#include*') {
        $doc.Range($p.Range.Start, $doc.Content.End).Delete()
        break
    }
}
Add-Para 'Listing Program.cs:'
Get-Content -LiteralPath $listingPath -TotalCount 55 -Encoding UTF8 | ForEach-Object {
    $line = $_
    $r = $doc.Content
    $r.Collapse(0)
    $r.InsertAfter("$line`r")
    $para = $doc.Paragraphs.Item($doc.Paragraphs.Count)
    $para.Range.Font.Name = 'Courier New'
    $para.Range.Font.Size = 10
    $para.Range.ParagraphFormat.FirstLineIndent = 0
    $para.Range.ParagraphFormat.LineSpacingRule = 0
}

$doc.Save()
$doc.Close()
$word.Quit()
Write-Host "Saved: $outDoc"
