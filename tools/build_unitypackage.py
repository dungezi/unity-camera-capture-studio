"""Build the local-install Unity asset package from the UPM package sources.

Usage: python tools/build_unitypackage.py [--check]
"""

from __future__ import annotations

import argparse
import gzip
import io
import json
import re
import tarfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Packages" / "com.camera-capture.studio"
VERSION = json.loads((SOURCE / "package.json").read_text(encoding="utf-8"))["version"]
OUTPUT = ROOT / "dist" / f"CameraCaptureStudio-{VERSION}.unitypackage"
ASSET_ROOT = "Assets/CameraCaptureStudio"
ROOT_GUID = "4a285b1d2e814e498db9f093b7c1cc02"
ROOT_META = (
    f"fileFormatVersion: 2\nguid: {ROOT_GUID}\nfolderAsset: yes\n"
    "DefaultImporter:\n  externalObjects: {}\n  userData:\n"
    "  assetBundleName:\n  assetBundleVariant:\n"
).encode("utf-8")

ASSETS = [
    (ASSET_ROOT, None, ROOT_META),
    (f"{ASSET_ROOT}/Editor", None, SOURCE / "Editor.meta"),
    (f"{ASSET_ROOT}/Fonts", None, SOURCE / "Fonts.meta"),
    *[
        (
            f"{ASSET_ROOT}/Editor/{name}",
            SOURCE / "Editor" / name,
            SOURCE / "Editor" / f"{name}.meta",
        )
        for name in (
            "CameraCaptureProcessor.cs",
            "CameraCaptureWindow.cs",
            "CameraCaptureStudio.Editor.asmdef",
            "CaptureFilter.shader",
            "CaptureText.shader",
        )
    ],
    (
        f"{ASSET_ROOT}/Fonts/BRUSHSCI.TTF",
        SOURCE / "Fonts" / "BRUSHSCI.TTF",
        SOURCE / "Fonts" / "BRUSHSCI.TTF.meta",
    ),
]


def read(data: bytes | Path) -> bytes:
    return data if isinstance(data, bytes) else data.read_bytes()


def guid_for(meta: bytes) -> str:
    match = re.search(rb"(?m)^guid: ([0-9a-f]{32})\s*$", meta)
    if match is None:
        raise ValueError("Asset metadata has no valid GUID")
    return match.group(1).decode("ascii")


def add_member(archive: tarfile.TarFile, name: str, data: bytes | None) -> None:
    info = tarfile.TarInfo(name)
    info.mtime = 0
    info.uid = info.gid = 0
    info.uname = info.gname = ""
    if data is None:
        info.type = tarfile.DIRTYPE
        info.mode = 0o755
        archive.addfile(info)
    else:
        info.mode = 0o644
        info.size = len(data)
        archive.addfile(info, io.BytesIO(data))


def build() -> bytes:
    buffer = io.BytesIO()
    seen: set[str] = set()
    with gzip.GzipFile(fileobj=buffer, filename="", mode="wb", mtime=0) as zipped:
        with tarfile.open(fileobj=zipped, mode="w", format=tarfile.GNU_FORMAT) as archive:
            for path, source, meta_source in sorted(ASSETS):
                metadata = read(meta_source)
                guid = guid_for(metadata)
                if guid in seen:
                    raise ValueError(f"Duplicate GUID: {guid}")
                seen.add(guid)
                add_member(archive, guid, None)
                if source is not None:
                    add_member(archive, f"{guid}/asset", source.read_bytes())
                add_member(archive, f"{guid}/asset.meta", metadata)
                add_member(archive, f"{guid}/pathname", path.encode("utf-8"))
    return buffer.getvalue()


def verify(data: bytes) -> None:
    expected = {path: (source, read(meta)) for path, source, meta in ASSETS}
    found: set[str] = set()
    with tarfile.open(fileobj=io.BytesIO(data), mode="r:gz") as archive:
        for member in archive.getmembers():
            if not member.name.endswith("/pathname"):
                continue
            guid = member.name.split("/", 1)[0]
            path = archive.extractfile(member).read().decode("utf-8")
            if path not in expected or path in found:
                raise ValueError(f"Unexpected or duplicate asset: {path}")
            source, metadata = expected[path]
            if guid_for(metadata) != guid:
                raise ValueError(f"GUID mismatch: {path}")
            if archive.extractfile(f"{guid}/asset.meta").read() != metadata:
                raise ValueError(f"Metadata mismatch: {path}")
            if source is not None and archive.extractfile(f"{guid}/asset").read() != source.read_bytes():
                raise ValueError(f"Asset mismatch: {path}")
            if source is None and f"{guid}/asset" in archive.getnames():
                raise ValueError(f"Folder has an asset payload: {path}")
            found.add(path)
    if found != expected.keys():
        raise ValueError(f"Missing assets: {sorted(expected.keys() - found)}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="check the committed package is current")
    args = parser.parse_args()
    built = build()
    verify(built)
    if args.check:
        if not OUTPUT.exists() or OUTPUT.read_bytes() != built:
            raise SystemExit(f"Outdated unitypackage: {OUTPUT}")
        print(f"Valid unitypackage: {OUTPUT}")
    else:
        OUTPUT.parent.mkdir(parents=True, exist_ok=True)
        OUTPUT.write_bytes(built)
        print(f"Wrote {OUTPUT} ({len(built)} bytes, {len(ASSETS)} assets)")


if __name__ == "__main__":
    main()
