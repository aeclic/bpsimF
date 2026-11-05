"""Prepare a pinned offline upstream editor; never install or enable the Mod."""
from __future__ import annotations

import argparse
import concurrent.futures
import hashlib
import json
import pathlib
import re
import shutil
import urllib.parse
import urllib.request
import zipfile

REVISION = "8697da72181ea1c7770c0b356c93d58678911e0a"
REPOSITORY = "https://github.com/LambdaEd1th/badpiggies-editor"
PAGES = "https://lambdaed1th.github.io"
MAIN_JS = "/badpiggies-editor/assets/badpiggies-editor-dxh90577879dbc2cb6.js"
MAIN_WASM = "/badpiggies-editor/assets/badpiggies-editor_bg-dxh11dcd55fe904f.wasm"
MAIN_CSS = "/badpiggies-editor/assets/dioxus-dxh54f6888ca7d3af6e.css"
HEADERS = {"User-Agent": "bpsim-level-editor-mod/0.1"}


def download(url: str) -> bytes:
    with urllib.request.urlopen(urllib.request.Request(url, headers=HEADERS), timeout=120) as response:
        return response.read()


def source_tree(work: pathlib.Path) -> pathlib.Path:
    source = work / "upstream"
    if (source / "Cargo.toml").exists():
        return source
    archive = work / "upstream.zip"
    if not archive.exists():
        archive.write_bytes(download(f"https://codeload.github.com/LambdaEd1th/badpiggies-editor/zip/{REVISION}"))
    source.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive) as package:
        for entry in package.infolist():
            parts = pathlib.PurePosixPath(entry.filename).parts[1:]
            if not parts or entry.is_dir():
                continue
            if any(part in {"..", ""} for part in parts):
                raise ValueError("Unsafe upstream archive entry")
            target = source.joinpath(*parts)
            if not target.resolve().is_relative_to(source.resolve()):
                raise ValueError("Upstream archive escapes the source directory")
            target.parent.mkdir(parents=True, exist_ok=True)
            with package.open(entry) as incoming, target.open("wb") as outgoing:
                shutil.copyfileobj(incoming, outgoing)
    return source


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", type=pathlib.Path, default=pathlib.Path(__file__).resolve().parents[2])
    args = parser.parse_args()
    project = args.project.resolve()
    mod = project / "Mods" / "LevelEditor"
    web = mod / "Web"
    work = project / "_codex_work" / "level-editor-mod"
    work.mkdir(parents=True, exist_ok=True)

    deployment = json.loads(download(
        "https://api.github.com/repos/LambdaEd1th/badpiggies-editor/deployments"
        "?environment=github-pages&per_page=1"
    ))
    if not deployment or deployment[0]["sha"] != REVISION:
        raise RuntimeError("Upstream Pages changed; review the adapter before updating its pinned revision.")
    source = source_tree(work)
    if list(source.rglob("AGENTS.md")):
        raise RuntimeError("Upstream has new agent instructions; inspect them before importing sources.")
    assets = web / "badpiggies-editor" / "assets" / "assets"
    shutil.copytree(source / "app" / "assets", assets, dirs_exist_ok=True)

    paths = [
        MAIN_JS, MAIN_WASM, MAIN_CSS,
        "/badpiggies-editor/assets/assets/worker/pkg/badpiggies_editor_worker.js",
        "/badpiggies-editor/assets/assets/worker/pkg/badpiggies_editor_worker_bg.wasm",
        "/badpiggies-editor/assets/assets/renderer/pkg/badpiggies_editor_renderer.js",
        "/badpiggies-editor/assets/assets/renderer/pkg/badpiggies_editor_renderer_bg.wasm",
    ]
    records: list[dict[str, object]] = []

    def fetch(path: str) -> tuple[str, bytes]:
        content = download(PAGES + path)
        destination = web.joinpath(*path.lstrip("/").split("/"))
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(content)
        return path, content

    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        fetched = list(pool.map(fetch, paths))
    # wasm-pack emits system-font helper modules alongside the renderer bindings.
    snippets = set()
    for path, content in fetched:
        if path.endswith(".js"):
            for relative in re.findall(r"""from\s*['"](\./snippets/[^'"]+\.js)['"]""", content.decode("utf-8")):
                snippets.add(urllib.parse.urljoin(path, relative))
    for path in sorted(snippets):
        fetched.append(fetch(path))
    for path, content in fetched:
        records.append({"path": path, "sha256": hashlib.sha256(content).hexdigest(), "bytes": len(content)})

    html = download(PAGES + "/badpiggies-editor/").decode("utf-8")
    if MAIN_JS not in html or MAIN_CSS not in html:
        raise RuntimeError("Published editor entry points do not match the pinned deployment.")
    html = html.replace("</head>", '<base href="/badpiggies-editor/">\n<link rel="stylesheet" href="/bpsim/editor.css">\n</head>', 1)
    marker = '<script type="module" async src="' + MAIN_JS + '"></script>'
    if marker not in html:
        raise RuntimeError("Cannot locate the upstream module bootstrap")
    html = html.replace(marker,
        '<script src="/bpsim/level-binary.js"></script>\n'
        '<script src="/bpsim/ultimate-water-renderer.js"></script>\n'
        '<script src="/bpsim/water-canvas.js"></script>\n'
        '<script src="/bpsim/editor-adapter.js"></script>\n' + marker, 1)
    (web / "index.html").write_text(html, encoding="utf-8", newline="\n")
    deployment_index = web / "badpiggies-editor" / "index.html"
    deployment_index.parent.mkdir(parents=True, exist_ok=True)
    deployment_index.write_text(html, encoding="utf-8", newline="\n")

    third_party = mod / "ThirdParty"
    third_party.mkdir(exist_ok=True)
    shutil.copyfile(source / "LICENSE", third_party / "BadPiggiesEditor.LICENSE")
    source_extensions = {".rs", ".toml", ".lock", ".md", ".ftl", ".wgsl", ".css", ".js", ".html", ".sh", ".yml", ".yaml"}
    with zipfile.ZipFile(third_party / "BadPiggiesEditor-Sources.zip", "w", zipfile.ZIP_DEFLATED) as package:
        for path in sorted(source.rglob("*")):
            if path.is_file() and (path.suffix in source_extensions or path.name == "LICENSE"):
                package.write(path, path.relative_to(source).as_posix())
    (third_party / "NOTICE.txt").write_text(
        f"Bad Piggies Editor: {REPOSITORY}\nRevision: {REVISION}\n"
        "Upstream editor code and published binaries: AGPL-3.0-or-later.\n"
        "See BadPiggiesEditor.LICENSE and BadPiggiesEditor-Sources.zip.\n"
        "The independent bpsim companion adapter observes the pinned editor's canvas host\n"
        "and imports files through its supported drag-and-drop file path; upstream WASM is unchanged.\n"
        "Game assets remain the property of Rovio and are NOT licensed under the AGPL.\n"
        "Vuplex is supplied by the game host and is not redistributed in this Mod.\n"
        "InteractiveWaterSystem source/assets are NOT included: author licensing is unresolved.\n",
        encoding="utf-8", newline="\n")
    (mod / "editor-artifacts.json").write_text(json.dumps({
        "repository": REPOSITORY, "revision": REVISION,
        "deployment_revision": deployment[0]["sha"], "artifacts": records,
        "adapter_contract": "bpEditorCanvas.render(ScenePayload), main/worker camera events, shared canvas overlay, supported file-drop import",
    }, indent=2) + "\n", encoding="utf-8")
    print(f"Prepared optional Mod assets: {web}")
    print(f"Verified deployment: {REVISION}")
    print(f"Downloaded {len(records)} published JS/WASM/CSS modules; no Mod was installed or enabled.")


if __name__ == "__main__":
    main()
