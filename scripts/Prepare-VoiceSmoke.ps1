# Offline fixture preparation only. Never calls Google or starts tests.
# Requires Windows SAPI with a pt-BR voice and an existing ffmpeg installation.
param([Parameter(Mandatory = $true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$ffmpeg = Get-Command ffmpeg -ErrorAction Stop
$destination = [IO.Path]::GetFullPath($OutputDirectory)
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($destination -eq $repository -or $destination.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use um diretório externo ao repositório para os áudios sintéticos.'
}
[IO.Directory]::CreateDirectory($destination) | Out-Null
foreach ($name in @('short', 'silence', 'unintelligible', 'fictional', 'invalid')) {
    if (Test-Path -LiteralPath (Join-Path $destination ($name + '.ogg'))) { throw 'Fixtures existentes: escolha um diretório novo.' }
}
$speaker = New-Object -ComObject SAPI.SpVoice
try {
    $portuguese = @($speaker.GetVoices() | Where-Object { $_.GetAttribute('Language') -match '(^|;)416($|;)' })
    if ($portuguese.Count -eq 0) { throw 'Instale uma voz pt-BR offline do Windows antes de preparar as fixtures.' }
    $speaker.Voice = $portuguese[0]
    function Convert-SyntheticSpeech([string]$Name, [string]$Text, [string]$Filter) {
        $wav = Join-Path $destination ([Guid]::NewGuid().ToString() + '.wav')
        $stream = New-Object -ComObject SAPI.SpFileStream
        try {
            $stream.Open($wav, 3, $false)
            $speaker.AudioOutputStream = $stream
            $speaker.Speak($Text, 0) | Out-Null
            $stream.Close()
            & $ffmpeg.Source -hide_banner -loglevel error -i $wav -af $Filter -ac 1 -ar 48000 -c:a libopus (Join-Path $destination ($Name + '.ogg'))
            if ($LASTEXITCODE -ne 0) { throw 'Falha na conversão offline para Opus.' }
        } finally {
            try { $stream.Close() } catch { }
            [Runtime.InteropServices.Marshal]::FinalReleaseComObject($stream) | Out-Null
            if (Test-Path -LiteralPath $wav) { Remove-Item -LiteralPath $wav }
        }
    }
    Convert-SyntheticSpeech 'short' 'Esta é uma mensagem sintética de teste.' 'anull'
    Convert-SyntheticSpeech 'fictional' 'O nome fictício é Aurora e o número é quarenta e dois.' 'anull'
    # Deliberately degraded synthetic speech; intelligibility must be checked in the real smoke.
    Convert-SyntheticSpeech 'unintelligible' 'Palavras sintéticas sem contexto para o ensaio.' 'atempo=2,atempo=2,highpass=f=3500,lowpass=f=3800'
    & $ffmpeg.Source -hide_banner -loglevel error -f lavfi -i 'anullsrc=r=48000:cl=mono' -t 2 -c:a libopus (Join-Path $destination 'silence.ogg')
    if ($LASTEXITCODE -ne 0) { throw 'Falha na criação do silêncio sintético.' }
    [IO.File]::WriteAllBytes((Join-Path $destination 'invalid.ogg'), [Text.Encoding]::ASCII.GetBytes('synthetic invalid audio'))
    Write-Output 'Cinco fixtures sintéticas preparadas fora do Git; nenhum teste ou envio externo foi executado.'
} finally {
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($speaker) | Out-Null
}
