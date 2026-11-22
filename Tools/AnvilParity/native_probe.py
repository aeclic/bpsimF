"""Probe the shipped native DLL in a private process/world; no Unity/editor state is touched."""
import argparse
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


WorldDef, BodyDef, WeldDef, DistanceDef, RevoluteDef, PrismaticDef, SphericalDef = (
    raw_type(name, size) for name, size in
    (("WorldDef", 152), ("BodyDef", 112), ("WeldDef", 128), ("DistanceDef", 160),
     ("RevoluteDef", 152), ("PrismaticDef", 152), ("SphericalDef", 184)))


def main():
    root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native-dll", type=Path, default=root / "Assets/Plugins/x86_64/box3d.dll")
    parser.add_argument("--report", type=Path, default=root / "Logs/anvil-native-contracts.json")
    args = parser.parse_args()
    dll = args.native_dll.resolve()
    api = c.CDLL(str(dll))

    def bind(name, result, *arguments):
        function = getattr(api, name)
        function.restype, function.argtypes = result, arguments
        return function

    default_world = bind("b3DefaultWorldDef", WorldDef)()
    default_body = bind("b3DefaultBodyDef", BodyDef)()
    default_weld = bind("b3DefaultWeldJointDef", WeldDef)()
    joint_defaults = {
        "DistanceDef": bind("b3DefaultDistanceJointDef", DistanceDef)(),
        "RevoluteDef": bind("b3DefaultRevoluteJointDef", RevoluteDef)(),
        "PrismaticDef": bind("b3DefaultPrismaticJointDef", PrismaticDef)(),
        "SphericalDef": bind("b3DefaultSphericalJointDef", SphericalDef)(),
    }
    create_world = bind("b3CreateWorld", c.c_uint32, c.POINTER(WorldDef))
    destroy_world = bind("b3DestroyWorld", None, c.c_uint32)
    set_warm_starting = bind("b3World_EnableWarmStarting", None, c.c_uint32, c.c_ubyte)
    get_warm_starting = bind("b3World_IsWarmStartingEnabled", c.c_ubyte, c.c_uint32)
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
        initial_warm_starting = bool(get_warm_starting(world))
        set_warm_starting(world, 0)
        disabled_warm_starting = not get_warm_starting(world)
        set_warm_starting(world, 1)
        restored_warm_starting = bool(get_warm_starting(world))
        report = {
            "dll_sha256": hashlib.sha256(dll.read_bytes()).hexdigest(),
            "sizes": {"WorldDef": c.sizeof(WorldDef), "BodyDef": c.sizeof(BodyDef),
                      "WeldDef": c.sizeof(WeldDef), "DistanceDef": c.sizeof(DistanceDef),
                      "RevoluteDef": c.sizeof(RevoluteDef), "PrismaticDef": c.sizeof(PrismaticDef),
                      "SphericalDef": c.sizeof(SphericalDef), "MassData": c.sizeof(Mass)},
            "defaults": {
                "maximumLinearSpeed": struct.unpack_from("<f", bytes(default_world.data), 40)[0],
                "restitutionThreshold": struct.unpack_from("<f", bytes(default_world.data), 12)[0],
                "contactHertz": struct.unpack_from("<f", bytes(default_world.data), 28)[0],
                "contactDampingRatio": struct.unpack_from("<f", bytes(default_world.data), 32)[0],
                "constraintHertz": struct.unpack_from("<f", bytes(default_weld.data), 88)[0],
                "constraintDampingRatio": struct.unpack_from("<f", bytes(default_weld.data), 92)[0],
            },
            "joint_default_internal_values": {
                name: struct.unpack_from("<i", bytes(definition.data), 104)[0]
                for name, definition in joint_defaults.items()
            },
            "positive_inertia_angular_response_rad_s": positive,
            "singular_inertia_angular_response_rad_s": singular,
            "warm_starting_transitions": [initial_warm_starting, disabled_warm_starting, restored_warm_starting],
            "pass": abs(positive - 2) <= 1e-6 and abs(singular) <= 1e-6
                    and initial_warm_starting and disabled_warm_starting and restored_warm_starting
                    and all(struct.unpack_from("<i", bytes(definition.data), 104)[0] == 1152023
                            for definition in joint_defaults.values()),
        }
        args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(report, indent=2))
        return 0 if report["pass"] else 1
    finally:
        destroy_world(world)


if __name__ == "__main__":
    raise SystemExit(main())
