import os, subprocess, sys

EXE = r'D:\folder\2022.2.23f1\Editor\Unity.exe'
PROJ = r'D:\rpgvx + magpie\BPRC 2026.3.2 proj\bpsim-main'
LOG = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'editor_stdout.log')

DROP = ('NO_PROXY', 'HTTP_PROXY', 'HTTPS_PROXY', 'ALL_PROXY')
env = {k: v for k, v in os.environ.items() if k.upper() not in DROP}

DETACHED_PROCESS = 0x00000008
CREATE_NEW_PROCESS_GROUP = 0x00000200

out = open(LOG, 'wb')
p = subprocess.Popen([EXE, '-projectPath', PROJ], env=env,
                     stdin=subprocess.DEVNULL, stdout=out, stderr=out,
                     creationflags=DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP)
print('launched pid', p.pid)
print('dropped proxy vars from child env')
