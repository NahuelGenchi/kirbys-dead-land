#!/bin/sh
# Adds Kirby's Dead Land from this folder to the applications menu. Run it again after moving the folder.
set -eu
here=$(cd "$(dirname "$0")" && pwd)
data=${XDG_DATA_HOME:-$HOME/.local/share}
mkdir -p "$data/applications" "$data/icons/hicolor/256x256/apps"
cp "$here/kirbys-dead-land.png" "$data/icons/hicolor/256x256/apps/kirbys-dead-land.png"
# Desktop entries quote these characters with two backslashes, and % with %%.
program=$(printf '%s' "$here/kirbys-dead-land" | sed -e 's/[\\"`$]/\\\\&/g' -e 's/%/%%/g')
{
    grep -v '^Exec=' "$here/kirbys-dead-land.desktop"
    printf 'Exec="%s"\n' "$program"
} > "$data/applications/kirbys-dead-land.desktop"
echo "Kirby's Dead Land is in the applications menu."
