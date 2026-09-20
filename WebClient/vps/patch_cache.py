#!/usr/bin/env python3
"""One-off fix for an already-installed VPS: makes nginx stop caching the runtime's boot file (dotnet.js) as 'immutable'.

Run on the VPS:   python3 patch_cache.py [/etc/nginx/conf.d/warriors.conf]
Idempotent. Backs the config up, validates it with `nginx -t`, and restores the backup if validation fails."""
import re, shutil, subprocess, sys

PATH = sys.argv[1] if len(sys.argv) > 1 else '/etc/nginx/conf.d/warriors.conf'
NEW = r'''    # Only FINGERPRINTED runtime files (name.<10 chars>.ext) never change under the same name. dotnet.js is not fingerprinted and lists the others by
    # name, so it must always be revalidated.
    location /_framework/ {
        add_header Cache-Control "no-cache";
    }
    location ~ "^/_framework/.+\.[a-z0-9]{10}\.(js|wasm|dat|json|dll|pdb)$" {
        add_header Cache-Control "public, max-age=31536000, immutable";
    }
'''

text = open(PATH).read()
if 'a-z0-9]{10}' in text:
    print('already patched'); sys.exit(0)

pattern = re.compile(r'(?:[ \t]*#[^\n]*\n)*[ \t]*location /_framework/ \{[^}]*\}\n')
if not pattern.search(text):
    print('could not find the /_framework/ block in', PATH); sys.exit(1)

shutil.copy(PATH, PATH + '.bak')
open(PATH, 'w').write(pattern.sub(lambda m: NEW, text, count=1))
if subprocess.run(['nginx', '-t']).returncode != 0:
    shutil.copy(PATH + '.bak', PATH)
    print('nginx rejected the new config - restored the old one'); sys.exit(1)
subprocess.run(['systemctl', 'reload', 'nginx'], check=True)
print('patched and reloaded (backup at %s.bak)' % PATH)
