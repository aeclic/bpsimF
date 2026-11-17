"""Probe the shipped native DLL in a private process/world; no Unity/editor state is touched."""
import ctypes as c
import hashlib
import json
import struct
from pathlib import Path


class Vec(c.Structure):
    _fields_ = [(axis, c.c_float) for axis in "xyz"]


class Matrix(c.Structure):
    _fields_ = [(axis, Vec) for axis in ("cx", "cy", "cz")]


class Mass(c.Structure):
    _fields_ = [("mass", c.c_float), ("center", Vec), ("inertia", Matrix)]


class BodyId(c.Structure):
    _fields_ = [("index1", c.c_int), ("world0", c.c_ushort), ("generation", c.c_ushort)]


def raw_type(name, size):
    return type(name, (c.Structure,), {"_fields_": [("data", c.c_ubyte * size)]})


WorldDef, BodyDef, WeldDef = (raw_type(name, size) for name, size in
                            (("WorldDef", 152), ("BodyDef", 112), ("WeldDef", 128)))


def main():
    root = Path(__file__).resolve().parents[2]
    dll = root / "Assets/Plugins/x86_64/box3d.dll"
    api = c.CDLL(str(dll))

    def bind(name, result, *arguments):
        function = getattr(api, name)
        function.restype, function.argtypes = result, arguments
        return function

    default_world = bind("b3DefaultWorldDef", WorldDef)()
    default_body = bind("b3DefaultBodyDef", BodyDef)()
    default_weld = bind("b3DefaultWeldJointDef", WeldDef)()
    create_world = bind("b3CreateWorld", c.c_uint32, c.POINTER(WorldDef))
    destroy_world = bind("b3DestroyWorld", None, c.c_uint32)
    create_body = bind("b3CreateBody", BodyId, c.c_uint32, c.POINTER(BodyDef))
    set_mass = bind("b3Body_SetMassData", None, BodyId, Mass)
    set_omega = bind("b3Body_SetAngularVelocity", None, BodyId, Vec)
    impulse = bind("b3Body_ApplyAngularImpulse", None, BodyId, Vec, c.c_ubyte)
    get_omega = bind("b3Body_GetAngularVelocity", Vec, BodyId)

    world_bytes = bytearray(default_world.data)
    struct.pack_into("<3f", world_bytes, 0, 0, 0, 0)
    world_bytes[64] = 0
    struct.pack_into("<I", world_bytes, 68, 1)
    world_def = WorldDef.from_buffer_copy(world_bytes)
    world = create_world(c.byref(world_def))
    if not world:
        raise RuntimeError("Native world creation failed")
    try:
        body_bytes = bytearray(default_body.data)
        struct.pack_into("<i", body_bytes, 0, 2)
        body_bytes[102] = 0
        body_def = BodyDef.from_buffer_copy(body_bytes)
        body = create_body(world, c.byref(body_def))
        set_mass(body, Mass(1, Vec(0, 0, 0), Matrix(Vec(1, 0, 0), Vec(0, 1, 0), Vec(0, 0, 1))))
        impulse(body, Vec(0, 0, 2), 1)
        positive = get_omega(body).z
        set_omega(body, Vec(0, 0, 0))
        set_mass(body, Mass(1, Vec(0, 0, 0), Matrix(Vec(0, 0, 0), Vec(0, 0, 0), Vec(0, 0, 1))))
        impulse(body, Vec(0, 0, 2), 1)
        singular = get_omega(body).z
        report = {
            "dll_sha256": hashlib.sha256(dll.read_bytes()).hexdigest(),
            "sizes": {"WorldDef": c.sizeof(WorldDef), "BodyDef": c.sizeof(BodyDef),
                      "WeldDef": c.sizeof(WeldDef), "MassData": c.sizeof(Mass)},
            "defaults": {
                "maximumLinearSpeed": struct.unpack_from("<f", bytes(default_world.data), 40)[0],
                "restitutionThreshold": struct.unpack_from("<f", bytes(default_world.data), 12)[0],
                "contactHertz": struct.unpack_from("<f", bytes(default_world.data), 28)[0],
                "contactDampingRatio": struct.unpack_from("<f", bytes(default_world.data), 32)[0],
                "constraintHertz": struct.unpack_from("<f", bytes(default_weld.data), 88)[0],
                "constraintDampingRatio": struct.unpack_from("<f", bytes(default_weld.data), 92)[0],
            },
            "positive_inertia_angular_response_rad_s": positive,
            "singular_inertia_angular_response_rad_s": singular,
            "pass": abs(positive - 2) <= 1e-6 and abs(singular) <= 1e-6,
        }
        path = root / "Logs/anvil-native-contracts.json"
        path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(report, indent=2))
        return 0 if report["pass"] else 1
    finally:
        destroy_world(world)


if __name__ == "__main__":
    raise SystemExit(main())
