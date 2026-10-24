# Third-Party Notices

Downio communicates with the following separately distributed executable over
its local JSON-RPC interface. That executable remains under its own license.

## aria2-next 3.0.8

- Project: <https://github.com/AnInsomniacy/aria2-next>
- Release/source: <https://github.com/AnInsomniacy/aria2-next/releases/tag/v3.0.8>
- License: GNU General Public License, version 2 or (at your option) any later version
- License copy in packaged applications: `licenses/aria2-next-GPL-2.0.txt` (in `Contents/Resources` on macOS, in `resources/` next to the executable on Windows and Linux)

The bundled `aria2c` executables are unmodified official binaries from the
v3.0.8 release. Downio's MIT license does not replace or restrict the license
terms that apply to aria2-next.

The bundled `server.met` and `nodes.dat` bootstrap files are sourced from the
Motrix Next project: <https://github.com/AnInsomniacy/motrix-next>.

## Windows 7 package only

The `win7-x64` build statically links the following libraries into `Downio.exe`.

- YY-Thunks 1.2.3: <https://github.com/Chuyu-Team/YY-Thunks>, MIT License
- VC-LTL 5.3.1: <https://github.com/Chuyu-Team/VC-LTL5>, Eclipse Public License 2.0

That package also carries Microsoft's redistributable Universal C Runtime DLLs
from the Windows SDK, for the bundled `aria2c.exe`.
