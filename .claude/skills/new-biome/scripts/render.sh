#!/bin/bash
# Render the 4 test chunks (EnemyChunk x2, Bridge, rotated Crossroads) with a tileset at the GAMEPLAY camera
# (FOV 14, offset (0,36,-36), rot 45) into a PNG, and print a count of spawned props per type.
# usage: render.sh <tileset asset path> "<sky C# color>" <tx> <tz> <out png>
# (edit-mode script when the Editor is not playing, SceneManager.CreateScene variant while playing)
D="$(cd "$(dirname "$0")" && pwd)"
cd "$(git -C "$D" rev-parse --show-toplevel)"
P=$(unity command eval --code 'return UnityEditor.EditorApplication.isPlaying + "";' 2>&1 | grep -o '"result":"[^"]*"')
if [ "$P" = '"result":"True"' ]; then src=$D/render_chunks_play.cs; else src=$D/render_chunks_edit.cs; fi
TMP=$(mktemp -t render).cs
sed -e "s|__TILESET__|$1|" -e "s|__SKY__|$2|" -e "s|__TX__|$3|" -e "s|__TZ__|$4|" -e "s|__OUT__|$5|" $src > $TMP
unity command eval --code "$(cat $TMP)" 2>&1 | grep -o '"result":"[^"]*"\|Error.*\|  [A-Z].*Exception.*' | head -3
