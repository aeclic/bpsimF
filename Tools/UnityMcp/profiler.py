"""Bounded, project-local, read-only access to Editor profiler JSONL snapshots."""

from datetime import datetime, timezone
import json
from pathlib import Path
import re


MAX_SAMPLES = 120
MAX_FILES = 4
MAX_TAIL_BYTES = 512 * 1024
SESSION = re.compile(r"[A-Za-z0-9_-]{0,96}\Z")
LOG_NAME = re.compile(r"editor-([A-Za-z0-9_-]{1,96})-(\d{6})\.jsonl\Z")


def validate_profiler_params(params=None):
    params = dict(params or {})
    unknown = params.keys() - {"count", "afterSequence", "sessionId"}
    if unknown:
        raise ValueError("Unknown profiler query parameters: " + ", ".join(sorted(unknown)))
    count = params.get("count", 10)
    after = params.get("afterSequence", 0)
    session = params.get("sessionId", "")
    if type(count) is not int or not 1 <= count <= MAX_SAMPLES:
        raise ValueError(f"count must be an integer between 1 and {MAX_SAMPLES}")
    if type(after) is not int or not 0 <= after <= 9007199254740991:
        raise ValueError("afterSequence must be a nonnegative safe integer")
    if not isinstance(session, str) or not SESSION.fullmatch(session):
        raise ValueError("Invalid profiler sessionId")
    return {"count": count, "afterSequence": after, "sessionId": session}


def select_samples(samples, session, params):
    latest_sequence = samples[-1]["sequence"] if samples else 0
    reset = bool(params["sessionId"] and params["sessionId"] != session) or params["afterSequence"] > latest_sequence
    after = 0 if reset else params["afterSequence"]
    eligible = [sample for sample in samples if sample["sequence"] > after]
    selected = eligible[:params["count"]] if after else eligible[-params["count"]:]
    oldest = samples[0]["sequence"] if samples else 0
    return {"schema": 1, "sessionId": session, "latest": samples[-1] if samples else None,
            "samples": selected, "latestSequence": latest_sequence, "oldestSequence": oldest,
            "nextSequence": selected[-1]["sequence"] if selected else latest_sequence,
            "cursorReset": reset, "historyTruncated": bool(after and oldest > after + 1),
            "hasMore": bool(after and len(eligible) > len(selected))}


def _reject_constant(value):
    raise ValueError("Non-finite JSON number: " + value)


def _tail_records(path):
    with path.open("rb") as stream:
        size = stream.seek(0, 2)
        offset = max(0, size - MAX_TAIL_BYTES)
        stream.seek(offset)
        data = stream.read(MAX_TAIL_BYTES)
    if offset:
        boundary = data.find(b"\n")
        data = data[boundary + 1:] if boundary >= 0 else b""
    # An appending writer may have only written part of its last UTF-8/JSON line.
    for line in data.split(b"\n")[:-1]:
        try:
            record = json.loads(line.decode("utf-8"), parse_constant=_reject_constant)
        except (ValueError, UnicodeError):
            continue
        if isinstance(record, dict):
            yield record


def read_profiler_log(project, params=None):
    params = validate_profiler_params(params)
    project = Path(project).resolve()
    directory = project / "Logs" / "Profiler"
    if not directory.resolve().is_relative_to(project):
        raise ValueError("Profiler log directory escapes the configured Unity project")
    files = []
    if directory.is_dir():
        for path in directory.iterdir():
            match = LOG_NAME.fullmatch(path.name)
            if match and path.is_file() and path.resolve().is_relative_to(project):
                files.append((path.name, path, match.group(1)))
    files.sort(key=lambda item: item[0])
    retained_sessions = sorted({item[2] for item in files})
    requested = params["sessionId"]
    session = requested if requested in retained_sessions else (retained_sessions[-1] if retained_sessions else "")
    current = [item for item in files if item[2] == session][-MAX_FILES:]
    by_sequence = {}
    errors = []
    for _, path, _ in current:
        try:
            for record in _tail_records(path):
                sequence = record.get("sequence")
                if (record.get("type") == "editor_profiler_sample" and type(record.get("schema")) is int and record["schema"] == 1
                        and record.get("editorOnly") is True and record.get("sessionId") == session
                        and type(sequence) is int and 1 <= sequence <= 9007199254740991
                        and isinstance(record.get("utc"), str)):
                    try:
                        stamp = datetime.fromisoformat(record["utc"].replace("Z", "+00:00"))
                        if stamp.tzinfo is None:
                            continue
                    except ValueError:
                        continue
                    by_sequence[sequence] = record
        except OSError as exc:
            errors.append(f"{path.name}: {exc}")
    samples = [by_sequence[key] for key in sorted(by_sequence)][-MAX_SAMPLES:]
    result = select_samples(samples, session, params)
    age = None
    if samples:
        stamp = datetime.fromisoformat(samples[-1]["utc"].replace("Z", "+00:00"))
        age = max(0, (datetime.now(timezone.utc) - stamp).total_seconds())
    result.update({"editorOnly": True, "enabled": None, "intervalSeconds": 1,
                   "historyCapacity": MAX_SAMPLES, "sampleAgeSeconds": age,
                   "freshness": "offline" if samples else "unavailable", "logAvailable": bool(current),
                   "log": {"directory": str(directory), "currentFile": str(current[-1][1]) if current else None,
                           "filesRead": len(current), "maxTailBytesPerFile": MAX_TAIL_BYTES,
                           "retainedSessions": [{"sessionId": item, "fileCount": sum(1 for entry in files if entry[2] == item)}
                                                for item in retained_sessions[-MAX_FILES:]]},
                   "errors": errors,
                   "semantics": "Persisted Editor samples, not a live connection or Player benchmark. Check sampleAgeSeconds and context; unavailable metrics are not zero."})
    return result
