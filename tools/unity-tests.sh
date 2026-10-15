#!/bin/sh
# Runs the EditMode tests in the open Unity editor and exits non-zero on any
# failure. Arguments pass through to unity_tests.py; see its docstring.
#   sh tools/unity-tests.sh                      refresh, compile, run every test assembly
#   sh tools/unity-tests.sh -g 'Ns\.Class\.'     the same, for one group
#   sh tools/unity-tests.sh --refresh-only       mutate.sh's BUILD step
#   sh tools/unity-tests.sh --no-refresh -g ...  mutate.sh's TEST step
for python in python3 python; do
  if "$python" -c 'import sys; sys.exit(sys.version_info < (3, 8))' > /dev/null 2>&1; then
    exec "$python" -I "$(dirname "$0")/unity_tests.py" "$@"
  fi
done
echo "unity-tests.sh: Python 3.8 or later is required" >&2
exit 2
