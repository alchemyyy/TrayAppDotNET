#!/bin/bash
# Compiles every diagram under plantuml/ (subfolders included) to an SVG beside its source, then recompiles each
# diagram whose source changes. dark-theme.puml is the shared include, not a diagram.
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PLANTUML_DIR="$SCRIPT_DIR/plantuml"
JAR="$SCRIPT_DIR/plantuml-mit-1.2026.2.jar"
PLANTUML_ARGS="-tsvg --ignore-startuml-filename"
THEME_FILE="$PLANTUML_DIR/dark-theme.puml"

get_mtime() {
    stat -c %Y "$1" 2>/dev/null || stat -f %m "$1" 2>/dev/null
}

list_diagrams() {
    find "$PLANTUML_DIR" -type f -name '*.puml' ! -path "$THEME_FILE" | sort
}

# With --ignore-startuml-filename the SVG is named after the source and written beside it.
get_svg_path() {
    echo "${1%.puml}.svg"
}

needs_compile() {
    local puml="$1"
    local svg
    svg=$(get_svg_path "$puml")
    [ ! -f "$svg" ] && return 0
    local puml_mt svg_mt theme_mt
    puml_mt=$(get_mtime "$puml")
    svg_mt=$(get_mtime "$svg")
    theme_mt=$(get_mtime "$THEME_FILE")
    [ "$puml_mt" -gt "$svg_mt" ] 2>/dev/null || [ "$theme_mt" -gt "$svg_mt" ] 2>/dev/null
}

compile() {
    # PlantUML writes the SVG beside its source when no output directory is given.
    java -jar "$JAR" $PLANTUML_ARGS "$1" &
    PIDS+=($!)
}

declare -A MTIMES
declare -A COMPILING

echo "Compiling stale .puml files..."
PIDS=()
while IFS= read -r f; do
    if needs_compile "$f"; then
        echo "Compiling: ${f#"$PLANTUML_DIR"/}"
        COMPILING[$f]=1
        compile "$f"
    fi
done < <(list_diagrams)
for pid in "${PIDS[@]}"; do wait "$pid"; done
while IFS= read -r f; do
    MTIMES[$f]=$(get_mtime "$f")
    unset "COMPILING[$f]"
done < <(list_diagrams)
THEME_MTIME=$(get_mtime "$THEME_FILE")
echo "Watching for changes (press Enter to force check, Ctrl+C to stop)..."

check_files() {
    declare -A SEEN
    PIDS=()
    COMPILE_LIST=()
    local theme_mtime theme_changed=""
    theme_mtime=$(get_mtime "$THEME_FILE")
    if [ "$theme_mtime" != "$THEME_MTIME" ]; then
        echo "Changed: dark-theme.puml — recompiling every diagram..."
        theme_changed=1
        THEME_MTIME="$theme_mtime"
    fi
    while IFS= read -r f; do
        SEEN[$f]=1
        [ -n "${COMPILING[$f]}" ] && continue
        mtime=$(get_mtime "$f")
        prev="${MTIMES[$f]}"
        if [ -z "$prev" ]; then
            echo "New: ${f#"$PLANTUML_DIR"/} — compiling..."
        elif [ "$mtime" != "$prev" ]; then
            echo "Changed: ${f#"$PLANTUML_DIR"/} — recompiling..."
        elif [ -z "$theme_changed" ]; then
            continue
        fi
        COMPILING[$f]=1
        COMPILE_LIST+=("$f")
        compile "$f"
        MTIMES[$f]="$mtime"
    done < <(list_diagrams)
    for pid in "${PIDS[@]}"; do wait "$pid"; done
    # Refresh mtimes after compile and clear dirty flags
    for f in "${COMPILE_LIST[@]}"; do
        MTIMES[$f]=$(get_mtime "$f")
        unset "COMPILING[$f]"
    done
    for f in "${!MTIMES[@]}"; do
        if [ -z "${SEEN[$f]}" ]; then
            echo "Removed: ${f#"$PLANTUML_DIR"/}"
            unset "MTIMES[$f]"
        fi
    done
    unset SEEN
}

while true; do
    if read -t 1 -s; then
        echo "Manual check..."
    fi
    check_files
done
