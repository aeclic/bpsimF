"""Probe the shipped destroy-joint wake argument in private native sleeping worlds.

Command: python -X utf8 Tools/AnvilStutter/probe_joint_lifecycle.py LABEL
PASS: both attached bodies sleep first; explicit wakeAttached=false preserves sleep,
      explicit true wakes both. Run independently twice and compare state counts/native hash.
Scope: x64 DLL, default weld and zero-gravity/zero-velocity bodies; not arbitrary joint types.
Invalidated by native hash/ABI changes or absent sleeping positive controls.
"""
import ctypes as c
import hashlib
import json
import pathlib
import struct
import sys
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools/AnvilParity'))
from native_probe import BodyDef, BodyId, Mass, Matrix, Vec, WeldDef, WorldDef


def main():
    label = sys.argv[1]
    assert label.replace('-', '').replace('_', '').isalnum()
    dll = ROOT / 'Assets/Plugins/x86_64/box3d.dll'
    api = c.CDLL(str(dll))

    def bind(name, result, *args):
        function = getattr(api, name)
        function.restype, function.argtypes = result, args
        return function

    default_world = bind('b3DefaultWorldDef', WorldDef)()
    default_body = bind('b3DefaultBodyDef', BodyDef)()
    default_weld = bind('b3DefaultWeldJointDef', WeldDef)()
    create_world = bind('b3CreateWorld', c.c_uint32, c.POINTER(WorldDef))
    destroy_world = bind('b3DestroyWorld', None, c.c_uint32)
    create_body = bind('b3CreateBody', BodyId, c.c_uint32, c.POINTER(BodyDef))
    set_mass = bind('b3Body_SetMassData', None, BodyId, Mass)
    create_weld = bind('b3CreateWeldJoint', BodyId, c.c_uint32, c.POINTER(WeldDef))
    destroy_joint = bind('b3DestroyJoint', None, BodyId, c.c_ubyte)
    awake = bind('b3Body_IsAwake', c.c_ubyte, BodyId)
    step = bind('b3World_Step', None, c.c_uint32, c.c_float, c.c_int)
    report = {'label': label, 'runId': uuid.uuid4().hex, 'nativeSha256': hashlib.sha256(dll.read_bytes()).hexdigest(),
              'probeSha256': hashlib.sha256(pathlib.Path(__file__).read_bytes()).hexdigest(), 'cases': [], 'failures': []}
    for wake in (0, 1):
        data = bytearray(default_world.data)
        struct.pack_into('<3f', data, 0, 0, 0, 0)
        data[64] = 1
        struct.pack_into('<I', data, 68, 1)
        world = create_world(c.byref(WorldDef.from_buffer_copy(data)))
        assert world
        try:
            bodies = []
            for _ in range(2):
                data = bytearray(default_body.data)
                struct.pack_into('<i3f', data, 0, 2, 0, 0, 0)
                data[102] = 1
                body = create_body(world, c.byref(BodyDef.from_buffer_copy(data)))
                set_mass(body, Mass(1, Vec(0, 0, 0), Matrix(Vec(1, 0, 0), Vec(0, 1, 0), Vec(0, 0, 1))))
                bodies.append(body)
            data = bytearray(default_weld.data)
            data[8:16], data[16:24] = bytes(bodies[0]), bytes(bodies[1])
            joint = create_weld(world, c.byref(WeldDef.from_buffer_copy(data)))
            assert joint.index1 > 0
            for _ in range(200): step(world, .02, 4)
            before = [int(awake(body)) for body in bodies]
            assert before == [0, 0], f'Sleeping positive control failed: {before}'
            destroy_joint(joint, wake)
            after = [int(awake(body)) for body in bodies]
            assert after == [wake, wake], f'Wake argument contract failed: {after}'
            report['cases'].append({'wakeAttached': wake, 'beforeAwake': before, 'afterAwake': after})
        except Exception as error:
            report['failures'].append(str(error))
        finally:
            destroy_world(world)
    path = ROOT / 'Tools/AnvilStutter/reports' / ('native-joint-lifecycle-' + label + '.json')
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report, ensure_ascii=False))
    assert len(report['cases']) == 2 and not report['failures']


if __name__ == '__main__':
    main()
