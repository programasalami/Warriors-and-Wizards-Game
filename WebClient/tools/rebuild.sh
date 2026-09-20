#!/bin/bash
# quick loop: rebuild + run one headless test. usage: tools/rebuild.sh [wait-seconds] [extra webtest args]
cd "$(dirname "$0")/.."
SC="C:/Users/cbart/AppData/Local/Temp/claude/C--Users-cbart-Desktop-Repos-Warriors-and-Wizards-Game/92da789d-c19b-4384-be39-eaac314dfde8/scratchpad"
python tools/build_web.py > "$SC/build_web.log" 2>&1 || { python tools/errs.py "$SC/build_web.log" 30 full | cut -c1-300 | sed 's#C:.Users.cbart.Desktop.[^\/]*.##'; tail -5 "$SC/build_web.log"; exit 1; }
tail -2 "$SC/build_web.log"
WAIT=${1:-30}; shift
python tools/webtest.py http://127.0.0.1:8124/ --wait "$WAIT" --shot "$SC/web.png" "$@" 2>&1 | cut -c1-330 | head -${LINES_MAX:-40}
