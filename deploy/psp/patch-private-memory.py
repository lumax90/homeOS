"""Linux PSP RAM uses an anonymous fd, not Docker's often-64-MiB /dev/shm.

This narrow POSIX memory-backing adaptation does not alter PSP emulation,
RetroArch, ROMs or saves. The complete modified source/recipe is shipped.
"""
from pathlib import Path
import subprocess
import sys

root = Path(sys.argv[1])
path = root / 'Common/MemArenaPosix.cpp'
original = subprocess.check_output(['git', '-C', str(root), 'show', 'HEAD:Common/MemArenaPosix.cpp']).decode()
patched = original.replace('#include <unistd.h>', '#include <unistd.h>\n#if defined(__linux__)\n#include <sys/syscall.h>\n#endif')
patched = patched.replace('bool is_shm = false;\n\tfor (int i = 0; i < 256; ++i)',
    'bool is_shm = false;\n#if defined(__linux__)\n'
    '\t// Home: anonymous RAM backing avoids /dev/shm SIGBUS with PSP-2000 RAM.\n'
    '\tfd = static_cast<int>(syscall(SYS_memfd_create, "home-ppsspp-ram", 1U));\n'
    '\tif (fd >= 0) {\n\t\tis_shm = true;\n\t\tINFO_LOG(Log::MemMap, "Home: using private anonymous PSP RAM backing");\n\t}\n'
    '#endif\n\tfor (int i = 0; fd < 0 && i < 256; ++i)')
if patched == original or 'Home: using private anonymous PSP RAM backing' not in patched:
    raise RuntimeError('Pinned PPSSPP memory adapter context changed.')
if path.read_text() not in (original, patched):
    raise RuntimeError('Unexpected modifications in cached PPSSPP source.')
path.write_text(patched)
