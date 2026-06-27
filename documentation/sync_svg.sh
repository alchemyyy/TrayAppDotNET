#!/bin/bash
# One-way sync watcher: copy SVG images from plantuml/ to .mdbook/
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
SRC="$SCRIPT_DIR/plantuml"
DST="$SCRIPT_DIR/.mdbook"

get_mtime() {
    stat -c %Y "$1" 2>/dev/null || stat -f %m "$1" 2>/dev/null
}

sync_svgs() {
    local copied=0
    for svg in "$SRC"/*.svg; do
        [ -f "$svg" ] || continue
        local name="$(basename "$svg")"
        local dst_file="$DST/$name"
        if [ ! -f "$dst_file" ] || [ "$(get_mtime "$svg")" -gt "$(get_mtime "$dst_file")" ]; then
            cp "$svg" "$dst_file"
            echo "Copied: $name"
            ((copied++))
        fi
    done
    [ "$copied" -gt 0 ] && echo "Synced $copied file(s)."
}

echo "Initial sync..."
sync_svgs
echo "Watching for changes (Ctrl+C to stop)..."

while true; do
    sleep 1
    sync_svgs
done
