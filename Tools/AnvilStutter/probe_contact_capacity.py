"""Probe the shipped native collector's capacity/purity contract in private process worlds.

Command: python -X utf8 Tools/AnvilStutter/probe_contact_capacity.py LABEL
PASS: clipped counts, identical full/prefix contact bytes under repeated reads, and
      saturation-driven growth reproduces all contacts for 0/1/24/64/65/128 sphere pairs.
Scope: the shipped x64 DLL and these contact fixtures; full geometry/impulse parity is separate.
Invalidated by native hash/ABI changes or changed collector return/side-effect behavior.
"""
import ctypes as c
import hashlib
import json
import pathlib
import struct
import sys
import time
import uuid


ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools/AnvilParity'))
from native_probe import BodyDef, BodyId, Mass, Matrix, Vec, WorldDef, raw_type


class Sphere(c.Structure):
    _fields_ = [('center', Vec), ('radius', c.c_float)]


class Contact(c.Structure):
    _fields_ = [('a', c.c_int), ('b', c.c_int), ('point', Vec), ('normal', Vec), ('impulse', c.c_float)]


def main():
    label = sys.argv[1]
    assert label.replace('-', '').replace('_', '').isalnum()
    dll = ROOT / 'Assets/Plugins/x86_64/box3d.dll'
    api = c.CDLL(str(dll))

    def bind(name, result, *args):
        function = getattr(api, name)
        function.restype, function.argtypes = result, args
        return function

    ShapeDef = raw_type('ShapeDef', 120)
    shape_def = bind('b3DefaultShapeDef', ShapeDef)()
    world_default = bind('b3DefaultWorldDef', WorldDef)()
    body_default = bind('b3DefaultBodyDef', BodyDef)()
    create_world = bind('b3CreateWorld', c.c_uint32, c.POINTER(WorldDef))
    destroy_world = bind('b3DestroyWorld', None, c.c_uint32)
    create_body = bind('b3CreateBody', BodyId, c.c_uint32, c.POINTER(BodyDef))
    create_sphere = bind('b3CreateSphereShape', BodyId, BodyId, c.POINTER(ShapeDef), c.POINTER(Sphere))
    set_mass = bind('b3Body_SetMassData', None, BodyId, Mass)
    step = bind('b3World_Step', None, c.c_uint32, c.c_float, c.c_int)
    collect = bind('anvil_CollectAllContacts', c.c_int, c.c_uint32, c.POINTER(Contact), c.c_int)
    get_counters = bind('b3World_GetCounters', raw_type('Counters', 200), c.c_uint32)
    report = {'label': label, 'runId': uuid.uuid4().hex, 'nativeSha256': hashlib.sha256(dll.read_bytes()).hexdigest(),
              'contactSize': c.sizeof(Contact), 'cases': [], 'failures': []}
    for pair_count in (0, 1, 24, 64, 65, 128):
        data = bytearray(world_default.data)
        struct.pack_into('<3f', data, 0, 0, -9.81, 0)
        data[64] = 0
        struct.pack_into('<I', data, 68, 1)
        definition = WorldDef.from_buffer_copy(data)
        world = create_world(c.byref(definition))
        assert world
        try:
            for i in range(pair_count):
                for dynamic in (False, True):
                    body_data = bytearray(body_default.data)
                    struct.pack_into('<i3f', body_data, 0, 2 if dynamic else 0, i * 2, .49 if dynamic else 0, 0)
                    body_data[102] = 0
                    body = create_body(world, c.byref(BodyDef.from_buffer_copy(body_data)))
                    sphere = Sphere(Vec(0, 0, 0), .25)
                    create_sphere(body, c.byref(shape_def), c.byref(sphere))
                    if dynamic:
                        set_mass(body, Mass(1, Vec(0, 0, 0), Matrix(Vec(.025, 0, 0), Vec(0, .025, 0), Vec(0, 0, .025))))
            for _ in range(24):
                step(world, .02, 4)
            full = (Contact * 4096)()
            full_count = collect(world, full, len(full))
            assert full_count == pair_count, f'Contact positive control: {full_count} != {pair_count}'
            full_bytes = bytes(full)[:full_count * c.sizeof(Contact)]
            capacities = []
            for capacity in (1, 7, 16, 32, 64, 65, 128, 129, 256):
                contacts = (Contact * capacity)()
                count = collect(world, contacts, capacity)
                assert count == min(capacity, full_count), f'Collector does not return clipped count: {count}'
                prefix = bytes(contacts)[:count * c.sizeof(Contact)]
                assert prefix == full_bytes[:len(prefix)], 'Capacity changed contact values/order'
                capacities.append({'capacity': capacity, 'count': count})
            capacity, reads = 64, 0
            while True:
                contacts = (Contact * capacity)()
                count = collect(world, contacts, capacity)
                reads += 1
                if count < capacity:
                    break
                capacity *= 2
            assert count == full_count and bytes(contacts)[:count * c.sizeof(Contact)] == full_bytes
            repeated = (Contact * 4096)()
            assert collect(world, repeated, len(repeated)) == full_count
            assert bytes(repeated)[:full_count * c.sizeof(Contact)] == full_bytes, 'Collector read had side effects'
            # Diagnostic only: identical-world call costs include Python/ctypes overhead.
            timings = {}
            for name, action in [('fullCounters', lambda: get_counters(world)), ('collector', lambda: collect(world, full, len(full)))]:
                start = time.perf_counter_ns()
                for _ in range(2000): action()
                timings[name + 'MeanUs'] = (time.perf_counter_ns() - start) / 2000 / 1000
            report['cases'].append({'pairs': pair_count, 'contacts': full_count, 'sha256': hashlib.sha256(full_bytes).hexdigest(),
                                    'capacities': capacities, 'adaptiveCapacity': capacity, 'adaptiveReads': reads, **timings})
        except Exception as error:
            report['failures'].append({'pairs': pair_count, 'error': str(error)})
        finally:
            destroy_world(world)
    path = ROOT / 'Tools/AnvilStutter/reports' / ('native-capacity-' + label + '.json')
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'report': str(path), 'cases': len(report['cases']), 'failures': report['failures']}, ensure_ascii=False))
    assert len(report['cases']) == 6 and not report['failures']


if __name__ == '__main__':
    main()
