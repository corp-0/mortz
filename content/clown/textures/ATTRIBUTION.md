# UnityStation tiles

Except for `signs/` and `stamps/`, the PNG files in this directory were copied from the UnityStation texture library:

- Source: https://github.com/unitystation/unitystation
- Revision: `eb15b606a0c112b1d51424d1f68f8866d997f96c`
- Original path: `UnityProject/Assets/Textures`
- License: [Creative Commons Attribution-ShareAlike 3.0](https://creativecommons.org/licenses/by-sa/3.0/)

UnityStation credits its asset contributors collectively through the project history and its
`AssetLibrary/attributions.txt` file.

Directional sprite sheets are split into `_south`, `_north`, `_east`, and `_west` files. The
unqualified filenames retain the original first frame for compatibility with existing maps.

The `signs/` textures were created for ClownStation using DejaVu Sans Mono. They contain original station signage, not UnityStation sprites.

The `stamps/` textures combine the UnityStation sprites above with station trim and signage. Composites containing UnityStation sprites retain the same CC BY-SA 3.0 attribution and license. Their component brushes are recorded in `stamps/sources.json`.
