"""Package the published executable with user documentation. No external dependencies."""
from pathlib import Path
import hashlib
import shutil
import zipfile

root = Path(__file__).resolve().parent.parent
artifacts = root / 'artifacts'
publish = artifacts / 'win-x64'
exe = publish / 'Dome.exe'
if not exe.is_file() or exe.read_bytes()[:2] != b'MZ':
    raise SystemExit('Missing Windows executable; publish the application first.')
for name in ('README.md', 'VALIDATION.md', 'LICENSE'):
    shutil.copyfile(root / name, publish / name)
archive = artifacts / 'Dome-windows-x64.zip'
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as output:
    for file in sorted(publish.iterdir()):
        if file.is_file():
            output.write(file, file.name)
files = (exe, archive)
(artifacts / 'SHA256SUMS.txt').write_text(''.join(
    f'{hashlib.sha256(file.read_bytes()).hexdigest()}  {file.relative_to(artifacts).as_posix()}\n' for file in files
))
print(f'Packaged {archive.name}: {archive.stat().st_size / 1048576:.1f} MiB')
