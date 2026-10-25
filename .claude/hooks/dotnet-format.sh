#!/usr/bin/env bash
# PostToolUse hook for Edit/Write: formats the edited C# file with dotnet format, using the nearest
# project, so style rules enforced by the build (.editorconfig) are fixed right away.
# Never blocks the edit: on any failure it exits 0 silently.

input=$(cat)
file=$(jq -r '.tool_input.file_path // empty' <<<"$input")
[[ "$file" == *.cs && -f "$file" ]] || exit 0
case "$file" in */bin/*|*/obj/*) exit 0 ;; esac

root="${CLAUDE_PROJECT_DIR:-$(git -C "$(dirname "$file")" rev-parse --show-toplevel 2>/dev/null)}"
[[ -n "$root" && "$file" == "$root"/* ]] || exit 0

# Nearest .csproj above the file; files outside a project (tools/*.cs) get whitespace fixes only
dir=$(dirname "$file") project=""
while [[ "$dir" == "$root"* ]]; do
  project=$(find "$dir" -maxdepth 1 -name '*.csproj' | head -1)
  [[ -n "$project" || "$dir" == "$root" ]] && break
  dir=$(dirname "$dir")
done

before=$(shasum "$file")
cd "$root" || exit 0
if [[ -n "$project" ]]; then
  dotnet format "$project" --include "${file#"$root"/}" --no-restore >/dev/null 2>&1
else
  dotnet format whitespace --folder --include "${file#"$root"/}" >/dev/null 2>&1
fi

if [[ "$(shasum "$file")" != "$before" ]]; then
  jq -n --arg f "${file#"$root"/}" '{hookSpecificOutput: {hookEventName: "PostToolUse",
    additionalContext: ("dotnet format reformatted " + $f + ": read it again before the next edit.")}}'
fi
exit 0
