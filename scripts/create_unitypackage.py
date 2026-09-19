#!/usr/bin/env python3
"""
create_unitypackage.py

Creates a standard Unity .unitypackage archive from a project folder without
requiring the Unity Editor or any external dependencies.

Unity .unitypackage format:
- POSIX tar archive compressed with gzip (.tar.gz)
- For every packaged file and folder:
  - {guid}/           (directory)
  - {guid}/pathname   (UTF-8 text containing the relative asset path in the Unity project)
  - {guid}/asset.meta (raw contents of the accompanying .meta file)
  - {guid}/asset      (raw binary content of the file; omitted for directories)
"""

import argparse
import io
import os
import re
from pathlib import Path
import sys
import tarfile

GUID_REGEX = re.compile(r"^guid:\s*([a-fA-F0-9]{32})\b", re.MULTILINE)


def extract_guid(meta_path: Path) -> str:
    """Extract 32-character hex GUID from a Unity .meta file."""
    try:
        content = meta_path.read_text(encoding="utf-8", errors="replace")
    except Exception as e:
        raise RuntimeError(f"Failed to read meta file {meta_path}: {e}") from e

    match = GUID_REGEX.search(content)
    if not match:
        raise ValueError(f"Could not find valid GUID in {meta_path}")
    return match.group(1).lower()


def should_skip(path: Path) -> bool:
    """
    Check if a file or folder should be excluded from the unitypackage:
    - Files/folders starting with '.' (hidden files, .git, etc.)
    - Folders ending with '~' (Unity ignored folders like Samples~, Documentation~)
    - .meta files (processed with their target asset)
    - Common editor temp files (*.tmp)
    """
    for part in path.parts:
        if part.startswith(".") and part != ".":
            return True
        if part.endswith("~"):
            return True
    if path.name.endswith(".meta") or path.name.endswith(".tmp"):
        return True
    return False


def build_unitypackage(
    project_dir: Path,
    package_rel_path: str,
    output_path: Path,
    verbose: bool = False,
) -> int:
    """
    Build a .unitypackage archive.

    :param project_dir: Root directory of the Unity project (containing Assets/).
    :param package_rel_path: Project-relative path to the package (e.g. 'Assets/DevSuite').
    :param output_path: Destination path for the generated .unitypackage file.
    :param verbose: If True, prints each packaged entry.
    :return: Total number of assets packaged.
    """
    project_dir = project_dir.resolve()
    target_dir = (project_dir / package_rel_path).resolve()

    if not target_dir.exists():
        raise FileNotFoundError(f"Package directory does not exist: {target_dir}")

    # Check root meta file (e.g., Assets/DevSuite.meta)
    root_meta = target_dir.with_name(target_dir.name + ".meta")
    if not root_meta.exists():
        raise FileNotFoundError(
            f"Root package .meta file does not exist: {root_meta}"
        )

    output_path = output_path.resolve()
    output_path.parent.mkdir(parents=True, exist_ok=True)

    # Collect assets
    entries_to_package = []

    # 1. Add the root package directory itself
    root_guid = extract_guid(root_meta)
    entries_to_package.append((target_dir, root_meta, root_guid, package_rel_path))

    # 2. Add all child assets
    missing_metas = []
    for item in sorted(target_dir.rglob("*")):
        if should_skip(item):
            continue

        meta_file = item.with_name(item.name + ".meta")
        if not meta_file.exists():
            missing_metas.append(item)
            continue

        guid = extract_guid(meta_file)
        rel_inside_pkg = item.relative_to(target_dir).as_posix()
        proj_asset_path = f"{package_rel_path}/{rel_inside_pkg}"
        entries_to_package.append((item, meta_file, guid, proj_asset_path))

    if missing_metas:
        print(f"Warning: Found {len(missing_metas)} assets without .meta files:")
        for m in missing_metas[:10]:
            print(f"  - {m}")
        if len(missing_metas) > 10:
            print(f"  ... and {len(missing_metas) - 10} more")

    print(
        f"Packaging {len(entries_to_package)} assets into {output_path.name}..."
    )

    file_count = 0
    dir_count = 0

    with tarfile.open(output_path, "w:gz", format=tarfile.GNU_FORMAT) as tar:
        for item_path, meta_path, guid, proj_path in entries_to_package:
            is_dir = item_path.is_dir()
            if is_dir:
                dir_count += 1
            else:
                file_count += 1

            if verbose:
                kind = "DIR " if is_dir else "FILE"
                print(f"[{kind}] {guid} -> {proj_path}")

            # 1. Guid directory entry
            guid_dir = tarfile.TarInfo(name=f"{guid}")
            guid_dir.type = tarfile.DIRTYPE
            guid_dir.mode = 0o755
            tar.addfile(guid_dir)

            # 2. pathname entry
            pathname_bytes = proj_path.encode("utf-8")
            pathname_info = tarfile.TarInfo(name=f"{guid}/pathname")
            pathname_info.size = len(pathname_bytes)
            pathname_info.mode = 0o644
            tar.addfile(pathname_info, io.BytesIO(pathname_bytes))

            # 3. asset.meta entry
            meta_bytes = meta_path.read_bytes()
            meta_info = tarfile.TarInfo(name=f"{guid}/asset.meta")
            meta_info.size = len(meta_bytes)
            meta_info.mode = 0o644
            tar.addfile(meta_info, io.BytesIO(meta_bytes))

            # 4. asset file entry (only for files, not directories)
            if not is_dir:
                file_bytes = item_path.read_bytes()
                asset_info = tarfile.TarInfo(name=f"{guid}/asset")
                asset_info.size = len(file_bytes)
                asset_info.mode = 0o644
                tar.addfile(asset_info, io.BytesIO(file_bytes))

    out_size = output_path.stat().st_size
    size_mb = out_size / (1024 * 1024)
    print(
        f"Successfully created {output_path} ({size_mb:.2f} MB, {out_size:,} bytes)."
    )
    print(
        f"Contents: {file_count} files, {dir_count} directories ({len(entries_to_package)} total assets)."
    )

    return len(entries_to_package)


def main():
    parser = argparse.ArgumentParser(
        description="Create a Unity .unitypackage archive without the Unity Editor."
    )
    parser.add_argument(
        "--project-dir",
        type=Path,
        default=None,
        help="Path to Unity project root (default: auto-detect 'DevSuite' or '.').",
    )
    parser.add_argument(
        "--package-dir",
        type=str,
        default="Assets/DevSuite",
        help="Project-relative path to package folder (default: Assets/DevSuite).",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=Path("DevSuite.unitypackage"),
        help="Destination path for the .unitypackage (default: DevSuite.unitypackage).",
    )
    parser.add_argument(
        "-v",
        "--verbose",
        action="store_true",
        help="Enable verbose output listing each packaged file.",
    )

    args = parser.parse_args()

    # Auto-detect project directory if not specified
    project_dir = args.project_dir
    if project_dir is None:
        if (Path("DevSuite") / args.package_dir).exists():
            project_dir = Path("DevSuite")
        elif Path(args.package_dir).exists():
            project_dir = Path(".")
        else:
            print(
                f"Error: Could not locate project directory containing {args.package_dir}.",
                file=sys.stderr,
            )
            sys.exit(1)

    try:
        build_unitypackage(
            project_dir=project_dir,
            package_rel_path=args.package_dir,
            output_path=args.output,
            verbose=args.verbose,
        )
    except Exception as e:
        print(f"Error creating unitypackage: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
