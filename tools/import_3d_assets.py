import argparse
import fnmatch
import hashlib
import json
import re
import sys
import tomllib
from pathlib import Path

import bpy


SCRIPT_VERSION = 3
IMAGE_EXTENSIONS = {".png", ".jpg", ".jpeg", ".tga", ".webp"}
NORMAL_SUFFIXES = ("_n", "_normal", "_norm", "-n", "-normal")
EMISSION_SUFFIXES = ("_e", "_emission", "_emissive", "-e", "-emission")
COLOR_SUFFIXES = ("_map", "_albedo", "_diffuse", "_color", "_basecolor", "_c", "_cd")


def parse_args():
    values = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--overrides", type=Path, required=True)
    parser.add_argument("--rebuild", action="store_true")
    return parser.parse_args(values)


def normalize(value):
    value = Path(value).stem.lower()
    value = re.sub(r"^(tex|mat)[_-]*", "", value)
    for suffix in NORMAL_SUFFIXES + EMISSION_SUFFIXES + COLOR_SUFFIXES:
        if value.endswith(suffix):
            value = value[: -len(suffix)]
            break
    return re.sub(r"[^a-z0-9]", "", value)


def role(path):
    stem = path.stem.lower()
    if stem.endswith(NORMAL_SUFFIXES):
        return "normal"
    if stem.endswith(EMISSION_SUFFIXES):
        return "emission"
    return "color"


def load_overrides(path):
    if not path.exists():
        return {}
    with path.open("rb") as handle:
        data = tomllib.load(handle)
    if data.get("format_version") != 1:
        raise RuntimeError(f"unsupported override format in {path}")
    return data.get("materials", {})


def discover_jobs(source_root):
    jobs = []
    for pack in sorted(path for path in source_root.iterdir() if path.is_dir()):
        blends = sorted(pack.rglob("*.blend"))
        blend_stems = {path.stem.lower() for path in blends}
        fbxs = sorted(path for path in pack.rglob("*.fbx") if path.stem.lower() not in blend_stems)
        for source in blends:
            jobs.append((pack, source, True))
        split_fbxs = len(fbxs) == 1
        for source in fbxs:
            jobs.append((pack, source, split_fbxs))
    return jobs


def image_candidates(pack):
    result = []
    for path in pack.rglob("*"):
        if path.is_file() and path.suffix.lower() in IMAGE_EXTENSIONS:
            if "promo materials" not in [part.lower() for part in path.parts]:
                result.append(path)
    return sorted(result)


def hash_job(source, textures, overrides_path):
    digest = hashlib.sha256(str(SCRIPT_VERSION).encode())
    for path in [source, *textures, overrides_path]:
        if not path.exists():
            continue
        digest.update(str(path).encode())
        with path.open("rb") as handle:
            for block in iter(lambda: handle.read(1024 * 1024), b""):
                digest.update(block)
    return digest.hexdigest()


def load_cache(path):
    if not path.exists():
        return {"version": SCRIPT_VERSION, "jobs": {}}
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {"version": SCRIPT_VERSION, "jobs": {}}
    if data.get("version") != SCRIPT_VERSION:
        return {"version": SCRIPT_VERSION, "jobs": {}}
    return data


def open_source(source):
    if source.suffix.lower() == ".blend":
        bpy.ops.wm.open_mainfile(filepath=str(source))
        return
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(source), use_image_search=True)


def file_for_image(image, candidates):
    if image is None:
        return None
    current = Path(bpy.path.abspath(image.filepath)) if image.filepath else None
    if current and current.is_file():
        return current
    wanted = Path(image.filepath).name.lower() if image.filepath else image.name.lower()
    matches = [path for path in candidates if path.name.lower() == wanted]
    return matches[0] if len(matches) == 1 else None


def find_node(material, node_type):
    return next((node for node in material.node_tree.nodes if node.type == node_type), None)


def socket(node, *names):
    for name in names:
        found = node.inputs.get(name)
        if found is not None:
            return found
    return None


def match_texture(pack, material, source, candidates, wanted_role, overrides):
    lookup = f"{pack.name}::{source.stem}::{material.name}"
    override = next(
        (value for pattern, value in overrides.items() if fnmatch.fnmatchcase(lookup, pattern)),
        None,
    )
    if override:
        path = pack / override
        if not path.is_file():
            return None, "override_missing", [str(path)]
        return path, "override", []

    pool = [path for path in candidates if role(path) == wanted_role]
    material_key = normalize(material.name)
    exact = [path for path in pool if normalize(path.name) == material_key]
    if len(exact) == 1:
        return exact[0], "material_name", []
    if len(exact) > 1:
        preferred = [path for path in exact if path.stem.lower().endswith("_map")]
        if len(preferred) == 1:
            return preferred[0], "material_name", []
        return None, "ambiguous", [str(path.relative_to(pack)) for path in exact]

    source_key = normalize(source.name)
    source_matches = [path for path in pool if normalize(path.name) == source_key]
    if len(source_matches) == 1:
        return source_matches[0], "source_name", []

    contained = [
        path for path in pool
        if material_key and (material_key in normalize(path.name) or normalize(path.name) in material_key)
    ]
    if len(contained) == 1:
        return contained[0], "partial_name", []
    return None, "unresolved", [str(path.relative_to(pack)) for path in contained]


def load_image(path, non_color=False):
    image = bpy.data.images.load(str(path), check_existing=True)
    image.filepath = str(path)
    if non_color:
        image.colorspace_settings.name = "Non-Color"
    return image


def link_image(material, principled, path, destination, non_color=False):
    image_node = material.node_tree.nodes.new("ShaderNodeTexImage")
    image_node.image = load_image(path, non_color)
    image_node.interpolation = "Closest"
    material.node_tree.links.new(image_node.outputs["Color"], destination)
    return image_node


def configure_material(pack, material, source, candidates, overrides):
    material.use_nodes = True
    nodes = material.node_tree.nodes
    principled = find_node(material, "BSDF_PRINCIPLED")
    if principled is None:
        principled = nodes.new("ShaderNodeBsdfPrincipled")
        output = find_node(material, "OUTPUT_MATERIAL") or nodes.new("ShaderNodeOutputMaterial")
        material.node_tree.links.new(principled.outputs["BSDF"], output.inputs["Surface"])

    base_input = socket(principled, "Base Color")
    linked_image_node = None
    if base_input and base_input.is_linked:
        linked_image_node = next(
            (link.from_node for link in base_input.links if link.from_node.type == "TEX_IMAGE"),
            None,
        )
    existing = file_for_image(linked_image_node.image if linked_image_node else None, candidates)
    if existing:
        linked_image_node.image = load_image(existing)
        color_path, color_method, alternatives = existing, "existing", []
    else:
        color_path, color_method, alternatives = match_texture(
            pack, material, source, candidates, "color", overrides
        )
        if color_path and base_input:
            if base_input.is_linked:
                for link in list(base_input.links):
                    material.node_tree.links.remove(link)
            linked_image_node = link_image(material, principled, color_path, base_input)

    normal_path = None
    if color_path:
        color_key = normalize(color_path.name)
        normal_matches = [
            path for path in candidates
            if role(path) == "normal" and normalize(path.name) == color_key
        ]
        normal_input = socket(principled, "Normal")
        if len(normal_matches) == 1 and normal_input and not normal_input.is_linked:
            normal_path = normal_matches[0]
            image_node = material.node_tree.nodes.new("ShaderNodeTexImage")
            image_node.image = load_image(normal_path, non_color=True)
            image_node.interpolation = "Closest"
            normal_node = material.node_tree.nodes.new("ShaderNodeNormalMap")
            material.node_tree.links.new(image_node.outputs["Color"], normal_node.inputs["Color"])
            material.node_tree.links.new(normal_node.outputs["Normal"], normal_input)

    emission_path = None
    if color_path:
        color_key = normalize(color_path.name)
        emission_matches = [
            path for path in candidates
            if role(path) == "emission" and normalize(path.name) == color_key
        ]
        emission_input = socket(principled, "Emission Color", "Emission")
        if len(emission_matches) == 1 and emission_input and not emission_input.is_linked:
            emission_path = emission_matches[0]
            link_image(material, principled, emission_path, emission_input)

    return {
        "material": material.name,
        "status": color_method,
        "color": str(color_path.relative_to(pack)) if color_path else None,
        "normal": str(normal_path.relative_to(pack)) if normal_path else None,
        "emission": str(emission_path.relative_to(pack)) if emission_path else None,
        "alternatives": alternatives,
    }


def safe_name(value):
    cleaned = re.sub(r"[<>:\"/\\|?*]", "_", value).strip(" .")
    return cleaned or "Mesh"


def descriptive_mesh_name(mesh):
    if not re.fullmatch(r"(Cube|Plane|Cylinder|Sphere|Torus)(\.\d+)?", mesh.name):
        return mesh.name
    material = next((slot.material for slot in mesh.material_slots if slot.material), None)
    return f"{material.name}_{mesh.name}" if material else mesh.name


def export_meshes(output_dir, source, split_objects):
    meshes = sorted(
        (obj for obj in bpy.context.scene.objects if obj.type == "MESH"),
        key=lambda obj: obj.name.lower(),
    )
    if not meshes:
        raise RuntimeError("source contains no mesh objects")

    output_dir.mkdir(parents=True, exist_ok=True)
    if not split_objects:
        output = output_dir / f"{safe_name(source.stem)}.glb"
        bpy.ops.object.select_all(action="DESELECT")
        for mesh in meshes:
            mesh.select_set(True)
        bpy.context.view_layer.objects.active = meshes[0]
        bpy.ops.export_scene.gltf(
            filepath=str(output),
            export_format="GLB",
            use_selection=True,
            export_cameras=False,
            export_lights=False,
        )
        return [output]

    used = set()
    outputs = []
    for mesh in meshes:
        name = safe_name(descriptive_mesh_name(mesh))
        base = name
        index = 2
        while name.lower() in used:
            name = f"{base}_{index}"
            index += 1
        used.add(name.lower())
        output = output_dir / f"{name}.glb"

        bpy.ops.object.select_all(action="DESELECT")
        mesh.select_set(True)
        bpy.context.view_layer.objects.active = mesh
        original = mesh.matrix_world.copy()
        mesh.matrix_world.translation = (0.0, 0.0, 0.0)
        bpy.ops.export_scene.gltf(
            filepath=str(output),
            export_format="GLB",
            use_selection=True,
            export_cameras=False,
            export_lights=False,
        )
        mesh.matrix_world = original
        outputs.append(output)
    return outputs


def remove_old_outputs(output_root, output_names):
    root = output_root.resolve()
    for value in output_names:
        path = (output_root / value).resolve()
        if root not in path.parents or path.suffix.lower() != ".glb":
            raise RuntimeError(f"unsafe cached output path: {path}")
        if path.exists():
            path.unlink()


def prune_empty_directories(output_root):
    if not output_root.exists():
        return
    directories = sorted(
        (path for path in output_root.rglob("*") if path.is_dir()),
        key=lambda path: len(path.parts),
        reverse=True,
    )
    for directory in directories:
        if not any(directory.iterdir()):
            directory.rmdir()


def main():
    args = parse_args()
    args.source_root = args.source_root.resolve()
    args.output_root = args.output_root.resolve()
    args.report = args.report.resolve()
    args.overrides = args.overrides.resolve()
    overrides = load_overrides(args.overrides)
    cache_path = args.source_root.parent / ".import-3d-cache.json"
    cache = load_cache(cache_path)
    report = {"format_version": 1, "jobs": [], "summary": {}}

    jobs = discover_jobs(args.source_root)
    converted = 0
    skipped = 0
    unresolved = 0
    active_keys = set()
    for pack, source, split_objects in jobs:
        textures = image_candidates(pack)
        key = str(source.relative_to(args.source_root)).replace("\\", "/")
        active_keys.add(key)
        fingerprint = hash_job(source, textures, args.overrides)
        cached = cache["jobs"].get(key, {})
        cached_outputs = cached.get("outputs", [])
        outputs_exist = cached_outputs and all((args.output_root / path).is_file() for path in cached_outputs)
        if not args.rebuild and cached.get("hash") == fingerprint and outputs_exist:
            print(f"SKIP {key}")
            material_results = cached.get("materials", [])
            cached_unresolved = sum(result.get("color") is None for result in material_results)
            unresolved += cached_unresolved
            report["jobs"].append({
                "source": key,
                "status": "skipped",
                "outputs": cached_outputs,
                "materials": material_results,
            })
            skipped += 1
            continue

        print(f"IMPORT {key}")
        open_source(source)
        material_results = []
        used_materials = {
            slot.material
            for mesh in bpy.context.scene.objects if mesh.type == "MESH"
            for slot in mesh.material_slots if slot.material is not None
        }
        for material in sorted(used_materials, key=lambda item: item.name.lower()):
            material_results.append(configure_material(pack, material, source, textures, overrides))
        unresolved += sum(result["color"] is None for result in material_results)

        remove_old_outputs(args.output_root, cached_outputs)
        output_dir = args.output_root / pack.name
        if split_objects:
            output_dir /= source.stem
        outputs = export_meshes(output_dir, source, split_objects)
        relative_outputs = [str(path.relative_to(args.output_root)).replace("\\", "/") for path in outputs]
        cache["jobs"][key] = {
            "hash": fingerprint,
            "outputs": relative_outputs,
            "materials": material_results,
        }
        report["jobs"].append({
            "source": key,
            "status": "converted",
            "outputs": relative_outputs,
            "materials": material_results,
        })
        converted += 1

    for stale_key in sorted(set(cache["jobs"]) - active_keys):
        print(f"REMOVE {stale_key}")
        remove_old_outputs(args.output_root, cache["jobs"][stale_key].get("outputs", []))
        del cache["jobs"][stale_key]

    report["summary"] = {
        "sources": len(jobs),
        "converted": converted,
        "skipped": skipped,
        "unresolved_materials": unresolved,
    }
    prune_empty_directories(args.output_root)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2), encoding="utf-8")
    cache_path.write_text(json.dumps(cache, indent=2), encoding="utf-8")
    print(
        f"DONE sources={len(jobs)} converted={converted} skipped={skipped} "
        f"unresolved_materials={unresolved}"
    )


main()
