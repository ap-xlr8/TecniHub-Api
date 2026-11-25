param(
    [string]$FilePath = (Join-Path $PSScriptRoot "..\..\CPdescargatxt\CPdescarga.txt"),
    [string]$ContainerName = "tecnihub-postgres-dev",
    [string]$Database = "tecnihub_dev",
    [string]$User = "tecnihub",
    [string]$PsqlConnectionString = ""
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $FilePath)) {
    throw "No se encontró el archivo SEPOMEX en: $FilePath"
}

$resolvedPath = (Resolve-Path $FilePath).Path
$tempCsv = Join-Path $env:TEMP "tecnihub_sepomex_import.tsv"

Write-Host "Preparando catálogo SEPOMEX desde $resolvedPath..."

# Detectar codificación (UTF-8 vs Windows-1252) y generar TSV limpio con timestamps UTC
python -c @"
import sys, datetime

src = sys.argv[1]
dst = sys.argv[2]
raw = open(src, 'rb').read()
try:
    text = raw.decode('utf-8-sig')
except UnicodeDecodeError:
    text = raw.decode('cp1252')

now_utc = datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%d %H:%M:%S+00')
seen = set()
count = 0

def clean(val, max_len):
    v = (val or '').strip().replace('\t', ' ').replace('\r', ' ').replace('\n', ' ')
    v = v.replace('\\', '\\\\')
    return v[:max_len]

with open(dst, 'w', encoding='utf-8', newline='\n') as out:
    for line in text.splitlines():
        parts = line.split('|')
        if len(parts) < 6:
            continue
        cp = parts[0].strip()
        if len(cp) != 5 or not cp.isdigit():
            continue
        neighborhood = clean(parts[1], 150)
        settlement_type = clean(parts[2], 80) or 'Colonia'
        municipality = clean(parts[3], 120)
        state = clean(parts[4], 80)
        city = clean(parts[5], 120)
        if not neighborhood or not municipality or not state:
            continue
        key = (cp, neighborhood.lower(), municipality.lower(), state.lower())
        if key in seen:
            continue
        seen.add(key)
        city_col = city if city else r'\N'
        out.write(f'{cp}\t{neighborhood}\t{settlement_type}\t{municipality}\t{state}\t{city_col}\t{now_utc}\t{now_utc}\n')
        count += 1

print(f'ROWS_PREPARED={count}')
"@ $resolvedPath $tempCsv

if ($LASTEXITCODE -ne 0) {
    throw "Falló la preparación del archivo SEPOMEX."
}

$sql = @"
BEGIN;
TRUNCATE TABLE postal_codes RESTART IDENTITY;
\copy postal_codes (postal_code, neighborhood, settlement_type, municipality, state, city, created_at, updated_at) FROM '/tmp/tecnihub_sepomex_import.tsv' WITH (FORMAT text, NULL '\N', ENCODING 'UTF8');
COMMIT;
SELECT COUNT(*) AS total_postal_codes, COUNT(DISTINCT postal_code) AS distinct_cps FROM postal_codes;
"@

if (-not [string]::IsNullOrWhiteSpace($PsqlConnectionString)) {
    $localSql = $sql.Replace('/tmp/tecnihub_sepomex_import.tsv', $tempCsv.Replace('\', '/'))
    $localSql | psql $PsqlConnectionString -v ON_ERROR_STOP=1
}
else {
    docker cp $tempCsv "${ContainerName}:/tmp/tecnihub_sepomex_import.tsv"
    if ($LASTEXITCODE -ne 0) { throw "Falló docker cp hacia $ContainerName." }
    $sql | docker exec -i $ContainerName psql -U $User -d $Database -v ON_ERROR_STOP=1
    if ($LASTEXITCODE -ne 0) { throw "Falló la importación COPY en PostgreSQL." }
    docker exec $ContainerName rm -f /tmp/tecnihub_sepomex_import.tsv | Out-Null
}

Remove-Item -Path $tempCsv -ErrorAction SilentlyContinue
Write-Host "Catálogo SEPOMEX importado correctamente."
