"""Independent, field-level audit of an RC2 export against its read-only APK.

Command: python Tools/RC2Levels/verify_source.py --apk SOURCE.apk --pack PACK_DIR --report REPORT.json
PASS: source node/component sets, TRS, renderer edges, decoded pixels, all mesh/skin
arrays and material/font/particle/animation fields agree; negative controls fail.
Boundary: serialized resources and static pose, NOT APK execution or dynamic pixels.
Invalidation: APK, UnityPy/IL2CPP layout, sprite decoder or document format changes.
"""
import argparse
import base64
import collections
import copy
import hashlib
import json
import math
import pathlib
import struct

from PIL import Image
from UnityPy.classes import PPtr
from UnityPy.export.SpriteHelper import get_image_from_sprite
from UnityPy.helpers.MeshHelper import MeshHandler

from apk_source import ApkSource, asset_key
from background_source import BackgroundSource


def canonical(value):
    if isinstance(value, float) and not math.isfinite(value):
        return {'source_ieee754_f32': struct.pack('<f', value).hex()}
    if isinstance(value, dict):
        return {k: canonical(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        return [canonical(v) for v in value]
    return value


def equal(actual, expected, label):
    expected = canonical(expected)
    if isinstance(actual, tuple):
        actual = list(actual)
    if isinstance(expected, dict):
        if not isinstance(actual, dict) or set(actual) != set(expected):
            raise AssertionError(f'{label}: object keys differ')
        for key in expected:
            equal(actual[key], expected[key], label + '.' + str(key))
    elif isinstance(expected, list):
        if not isinstance(actual, list) or len(actual) != len(expected):
            raise AssertionError(f'{label}: array length {len(actual) if isinstance(actual, list) else None} != {len(expected)}')
        for i, value in enumerate(expected):
            equal(actual[i], value, f'{label}[{i}]')
    elif actual != expected:
        raise AssertionError(f'{label}: {str(actual)[:160]} != {str(expected)[:160]}')


def vec(value, names):
    return [float(getattr(value, n)) for n in names]


def flatten(values):
    return [float(v) for row in values for v in row]


class Audit:
    def __init__(self, apk, root):
        self.root = pathlib.Path(root).resolve()
        self.catalog = self.load('catalog.json')
        self.source = ApkSource(apk, generate_trees=True)
        self.background_source = BackgroundSource(self.source)
        equal(hashlib.sha256(pathlib.Path(apk).read_bytes()).hexdigest(), self.catalog['apk_sha256'], 'apk hash')
        self.atlases = {}
        self.assets = {}
        self.mesh_handlers = {}
        self.sprite_results = {}
        self.stats = collections.Counter()
        self.backgrounds = {}
        for reader in self.source.readers.values():
            if reader.type.name == 'SpriteAtlas':
                for key, data in reader.read().m_RenderDataMap:
                    self.atlases[repr(key)] = reader, data

    def path(self, relative):
        path = (self.root / relative).resolve()
        if not path.is_relative_to(self.root):
            raise AssertionError('Escaping pack path: ' + relative)
        return path

    def load(self, relative):
        return json.loads(self.path(relative).read_text(encoding='utf-8'))

    def reader(self, key):
        file, pid = key.rsplit('_', 1)
        return self.source.readers[file, int(pid)]

    def asset_source(self, kind, key):
        if key is None:
            return None
        return self.catalog['assets'][kind][key]['source']

    def sprite(self, reader):
        key = asset_key(reader)
        if key in self.sprite_results:
            return self.sprite_results[key]
        data = reader.read()
        atlas = self.source.deref(data.m_SpriteAtlas, reader.assets_file)
        if atlas:
            rd = next(v for k, v in atlas.read().m_RenderDataMap if k == data.m_RenderDataKey)
        else:
            rd = data.m_RD
        if not rd.texture.m_PathID:
            atlas, rd = self.atlases[repr(data.m_RenderDataKey)]
        # Let UnityPy's separate SpriteHelper implementation decode/rotate/mask the
        # original atlas. Supply only the otherwise missing atlas pointer in memory.
        if atlas:
            data.m_SpriteAtlas = PPtr(m_FileID=0, m_PathID=atlas.path_id, assetsfile=atlas.assets_file)
        data.assets_file._cache.clear()  # upstream cache ids omit the external file
        image = get_image_from_sprite(data).convert('RGBA')
        file = atlas.assets_file if atlas else reader.assets_file
        tex = self.source.deref(rd.texture, file)
        alpha = self.source.deref(rd.alphaTexture, file)
        self.sprite_results[key] = data, rd, image, tex, alpha
        return self.sprite_results[key]

    def mesh(self, reader, data, label):
        key = asset_key(reader)
        if key not in self.mesh_handlers:
            original = reader.read()
            handler = MeshHandler(original.m_RD if reader.type.name == 'Sprite' else original, reader.assets_file.version)
            handler.process()
            self.mesh_handlers[key] = original, handler
        original, handler = self.mesh_handlers[key]
        vertices = flatten(handler.m_Vertices)
        count = len(vertices) // 3
        equal(data['vertices'], vertices, label + '.vertices')
        equal(data['source_uv'], flatten(handler.m_UV0 or [(0, 0)] * count), label + '.source_uv')
        equal(data['colors'], flatten(handler.m_Colors or [(1, 1, 1, 1)] * count), label + '.colors')
        equal(data['submeshes'], [[int(i) for tri in sub for i in tri] for sub in handler.get_triangles()], label + '.submeshes')
        equal(data['source_vertices_f32_sha256'], hashlib.sha256(struct.pack('<' + str(len(vertices)) + 'f', *vertices)).hexdigest(), label + '.vertices hash')
        if reader.type.name != 'Sprite' and original.m_BindPose and handler.m_BoneWeights:
            equal(data['bindposes'], [[float(getattr(m, f'e{r}{c}')) for c in range(4) for r in range(4)] for m in original.m_BindPose], label + '.bindposes')
            equal(data['weights'], flatten(handler.m_BoneWeights), label + '.weights')
            equal(data['bone_indices'], [int(v) for row in handler.m_BoneIndices for v in row], label + '.bone_indices')
        elif any(k in data for k in ('weights', 'bone_indices', 'bindposes')):
            raise AssertionError(label + ': invented skin data')

    def material(self, reader, data, label):
        original = reader.read()
        equal(data['name'], original.m_Name, label + '.name')
        equal(data['queue'], original.m_CustomRenderQueue, label + '.queue')
        props = original.m_SavedProperties
        equal(data['floats'], {k: float(v) for k, v in props.m_Floats}, label + '.floats')
        equal(data['colors'], {k: vec(v, 'rgba') for k, v in props.m_Colors}, label + '.colors')
        equal(sorted(data['textures']), sorted(k for k, _ in props.m_TexEnvs), label + '.texture slots')
        for slot, value in props.m_TexEnvs:
            texture = self.source.deref(value.m_Texture, reader.assets_file)
            equal(self.asset_source('textures', data['textures'][slot]['asset']), asset_key(texture) if texture else None, label + '.' + slot + '.source')
            equal(data['textures'][slot]['scale'], vec(value.m_Scale, 'xy'), label + '.' + slot + '.scale')
            equal(data['textures'][slot]['offset'], vec(value.m_Offset, 'xy'), label + '.' + slot + '.offset')
        if data.get('source_shader'):
            shader = self.reader(data['source_shader'])
            equal(data['source_shader_sha256'], hashlib.sha256(shader.get_raw_data()).hexdigest(), label + '.shader hash')
            equal(data['source_shader_properties'], shader.read_typetree()['m_ParsedForm']['m_PropInfo']['m_Props'], label + '.shader properties')
            equal(data['shader'], shader.read().m_ParsedForm.m_Name, label + '.shader name')

    def verify_assets(self):
        equal(self.catalog['source_members'], self.source.members, 'source members')
        for kind, entries in self.catalog['assets'].items():
            for key, meta in entries.items():
                label = kind + '/' + key
                path = self.path(meta['path'])
                equal(hashlib.sha256(path.read_bytes()).hexdigest(), meta['sha256'], label + '.file hash')
                reader = self.reader(meta['source'])
                equal(hashlib.sha256(reader.get_raw_data()).hexdigest(), meta['source_raw_sha256'], label + '.raw hash')
                if kind == 'textures':
                    image = Image.open(path).convert('RGBA')
                    original = self.sprite(reader)[2] if meta['sprite'] else reader.read().image.convert('RGBA')
                    equal(list(image.size), list(original.size), label + '.source dimensions')
                    equal(list(image.size), [meta['width'], meta['height']], label + '.dimensions')
                    equal(hashlib.sha256(image.tobytes()).hexdigest(), hashlib.sha256(original.tobytes()).hexdigest(), label + '.decoded source pixels')
                    equal(meta['rgba_sha256'], hashlib.sha256(original.tobytes()).hexdigest(), label + '.pixel hash')
                    if meta['sprite']:
                        _, rd, _, tex, alpha = self.sprite(reader)
                        equal(meta['source_atlas_texture'], asset_key(tex), label + '.atlas')
                        equal(meta['source_alpha_texture'], asset_key(alpha) if alpha else None, label + '.alpha')
                        equal(meta['packing'], rd.settingsRaw, label + '.packing')
                else:
                    data = self.load(meta['path'])
                    self.assets[kind, key] = data
                    if kind == 'meshes':
                        self.mesh(reader, data, label)
                        self.stats['vertices'] += len(data['vertices']) // 3
                        # A sprite coordinate change is checked against that original
                        # Sprite at every renderer edge below, not only against a hash.
                        if '_sprite_' not in key:
                            equal(data['uv'], data['source_uv'], label + '.unmodified uv')
                    elif kind == 'materials':
                        self.material(reader, data, label)
                    elif kind == 'fonts':
                        original = self.source.data(reader)
                        equal(data['data'], original, label + '.data')
                        equal(data['name'], original['m_Name'], label + '.name')
                        pointers = original.get('m_AtlasTextures') or ([original['atlas']] if original.get('atlas', {}).get('m_PathID') else [])
                        equal([self.asset_source('textures', t) for t in data['textures']], [asset_key(self.source.deref(p, reader.assets_file)) for p in pointers], label + '.atlas textures')
                        equal(self.asset_source('materials', data['material']), asset_key(self.source.deref(original['material'], reader.assets_file)), label + '.font material')
                        pointers = original.get('m_FallbackFontAssetTable') or original.get('fallbackFontAssets', [])
                        equal([self.asset_source('fonts', t) for t in data['fallbacks']], [asset_key(r) for p in pointers if (r := self.source.deref(p, reader.assets_file))], label + '.fallbacks')
                    elif kind == 'animations':
                        equal(data, reader.read_typetree(), label + '.clip')
                self.stats[kind] += 1

    def source_nodes(self, reader, parent=-1, rows=None):
        rows = [] if rows is None else rows
        go = reader.read()
        components = [self.source.deref(p, reader.assets_file) for p in go.m_Components]
        transform = next(c for c in components if c.type.name in ('Transform', 'RectTransform'))
        index = len(rows)
        rows.append((reader, components, transform, parent))
        for pointer in transform.read().m_Children:
            child = self.source.deref(pointer, transform.assets_file)
            self.source_nodes(self.source.deref(child.read().m_GameObject, child.assets_file), index, rows)
        return rows

    def node(self, node, row, nodes, prefabs, transforms, label):
        reader, components, tr, parent = row
        go, transform = reader.read(), tr.read()
        equal(node['source_game_object']['data'], reader.read_typetree(check_read=True), label + '.original GameObject data')
        equal(node['source_game_object']['raw_base64'], base64.b64encode(reader.get_raw_data()).decode('ascii'), label + '.original GameObject bytes')
        native = [c for c in components if c.type.name != 'MonoBehaviour']
        equal([c['source'] for c in node['source_components']], [asset_key(c) for c in native], label + '.all native component owners')
        for record, component in zip(node['source_components'], native):
            equal(record['type'], component.type.name, label + '.native type')
            equal(record['data'], component.read_typetree(check_read=True), label + '.native data/' + component.type.name)
            equal(record['raw_base64'], base64.b64encode(component.get_raw_data()).decode('ascii'), label + '.native raw/' + component.type.name)
            self.stats['native_components'] += 1
        for field, value in [('id', asset_key(reader)), ('name', go.m_Name), ('active', bool(go.m_IsActive)), ('layer', go.m_Layer), ('parent', parent),
                             ('p', vec(transform.m_LocalPosition, 'xyz')), ('q', vec(transform.m_LocalRotation, 'xyzw')), ('s', vec(transform.m_LocalScale, 'xyz'))]:
            equal(node[field], value, label + '.' + field)
        if tr.type.name == 'RectTransform':
            equal(node['rect'], {'size': vec(transform.m_SizeDelta, 'xy'), 'pivot': vec(transform.m_Pivot, 'xy'), 'anchor_min': vec(transform.m_AnchorMin, 'xy'), 'anchor_max': vec(transform.m_AnchorMax, 'xy')}, label + '.rect')
        scripts = [c for c in components if c.type.name == 'MonoBehaviour']
        equal([s['source'] for s in node['scripts']], [asset_key(c) for c in scripts], label + '.script owners')
        anima = None
        for record, c in zip(node['scripts'], scripts):
            script = self.source.script(c)
            equal(record['type'], script.m_ClassName, label + '.script type')
            equal(record['namespace'], script.m_Namespace, label + '.script namespace')
            equal(record['raw_base64'], base64.b64encode(c.get_raw_data()).decode('ascii'), label + '.script bytes')
            if 'data' in record:
                equal(record['data'], self.source.data(c), label + '.script data/' + record['type'])
            if script.m_ClassName == 'SpriteMeshInstance':
                anima = c, self.source.data(c)
            elif script.m_ClassName == 'FluidObject':
                equal(node['fluid']['data'], self.source.data(c), label + '.fluid data')
                self.stats['fluid_objects'] += 1
            elif script.m_ClassName == 'TextMeshPro':
                text = self.source.data(c)
                equal(node['text']['data'], text, label + '.text')
                equal(self.asset_source('fonts', node['text']['font']), asset_key(self.source.deref(text['m_fontAsset'], c.assets_file)), label + '.text font')
                equal(self.asset_source('materials', node['text']['material']), asset_key(self.source.deref(text['m_sharedMaterial'], c.assets_file)), label + '.text material')
                self.stats['text_objects'] += 1
        self.stats['scripts'] += len(scripts)
        expected_colliders = [c for c in components if 'Collider' in c.type.name]
        equal(node['colliders'], [{'type': c.type.name, 'data': c.read_typetree()} for c in expected_colliders], label + '.colliders')
        self.stats['colliders'] += len(expected_colliders)
        renderers = [c for c in components if c.type.name in ('SpriteRenderer', 'MeshRenderer', 'SkinnedMeshRenderer')]
        equal([r['id'] for r in node['renderers']], [asset_key(c) for c in renderers], label + '.renderer set')
        for record, c in zip(node['renderers'], renderers):
            original = c.read()
            prefab = prefabs[record['prefab']]
            equal(prefab['kind'], c.type.name, label + '.renderer kind')
            for field, value in [('enabled', bool(original.m_Enabled)), ('sorting_layer', original.m_SortingLayer), ('sorting_layer_id', original.m_SortingLayerID), ('sorting_order', original.m_SortingOrder)]:
                equal(record[field], value, label + '.renderer.' + field)
            sprite, mesh = None, None
            material_file, material_pointers = c.assets_file, original.m_Materials
            color = vec(original.m_Color, 'rgba') if c.type.name == 'SpriteRenderer' else [1, 1, 1, 1]
            if c.type.name == 'SpriteRenderer':
                sprite = self.source.deref(original.m_Sprite, c.assets_file)
                mesh = sprite
                for field, value in [('flip_x', original.m_FlipX), ('flip_y', original.m_FlipY), ('draw_mode', original.m_DrawMode), ('size', vec(original.m_Size, 'xy'))]:
                    equal(record[field], value, label + '.sprite.' + field)
            elif anima:
                instance, smi = anima
                sm = self.source.deref(smi['m_SpriteMesh'], instance.assets_file)
                smdata = self.source.data(sm)
                mesh = self.source.deref(smdata['m_SharedMesh'], sm.assets_file)
                sprite = self.source.deref(smdata['m_Sprite'], sm.assets_file)
                color = [smi['m_Color'][k] for k in 'rgba']
                material_file, material_pointers = instance.assets_file, smi['m_Materials']
                equal(record['bones'], [transforms[asset_key(self.source.deref(p, instance.assets_file))] for p in smi['m_BoneTransforms']], label + '.anima bones')
                self.stats['anima_resolved'] += 1
            elif c.type.name == 'SkinnedMeshRenderer':
                mesh = self.source.deref(original.m_Mesh, c.assets_file)
                equal(record['bones'], [transforms[asset_key(self.source.deref(p, c.assets_file))] for p in original.m_Bones], label + '.skin bones')
            else:
                mf = next((o for o in components if o.type.name == 'MeshFilter'), None)
                mesh = self.source.deref(mf.read().m_Mesh, mf.assets_file) if mf else None
            equal(record['color'], color, label + '.instance color')
            equal(self.asset_source('meshes', prefab['mesh']), asset_key(mesh) if mesh else None, label + '.mesh source')
            equal(self.asset_source('textures', prefab['texture']), asset_key(sprite) if sprite else None, label + '.sprite source')
            source_materials = [self.source.deref(p, material_file) for p in material_pointers]
            equal([self.asset_source('materials', m) for m in prefab['materials']], [asset_key(m) for m in source_materials if m], label + '.material sources')
            text_generated = bool(node.get('text')) or any(self.source.script(s).m_ClassName == 'TMP_SubMesh' for s in scripts)
            equal(prefab['text_generated'], text_generated, label + '.text generator')
            equal(prefab['fluid_generated'], bool(node.get('fluid')), label + '.fluid generator')
            equal(bool(record.get('source_empty')), mesh is None and not text_generated and not node.get('fluid'), label + '.source empty')
            if record.get('source_empty'):
                self.stats['source_empty_renderers'] += 1
            if sprite:
                s, rd, image, _, _ = self.sprite(sprite)
                expected = {'name': s.m_Name, 'ppu': s.m_PixelsToUnits, 'rect': [s.m_Rect.x, s.m_Rect.y, s.m_Rect.width, s.m_Rect.height], 'pivot': vec(s.m_Pivot, 'xy'),
                            'trim_offset': vec(rd.textureRectOffset, 'xy'), 'image_size': list(image.size), 'border': vec(s.m_Border, 'xyzw')}
                equal(prefab['sprite'], expected, label + '.sprite metadata')
                vertices = self.assets['meshes', prefab['mesh']]['vertices']
                px, py = expected['pivot']; _, _, rw, rh = expected['rect']; ox, oy = expected['trim_offset']; iw, ih = image.size
                uv = [v for i in range(0, len(vertices), 3) for v in ((vertices[i] * s.m_PixelsToUnits + rw * px - ox) / iw, (vertices[i + 1] * s.m_PixelsToUnits + rh * py - oy) / ih)]
                equal(self.assets['meshes', prefab['mesh']]['uv'], uv, label + '.cutout uv')
        self.stats['renderers'] += len(renderers)
        particle = next((c for c in components if c.type.name == 'ParticleSystem'), None)
        equal(node.get('particles') is not None, particle is not None, label + '.particle presence')
        if particle:
            data = node['particles']
            equal(data['source'], asset_key(particle), label + '.particle source')
            equal(data['data'], particle.read_typetree(), label + '.particle modules')
            pr = next((c for c in components if c.type.name == 'ParticleSystemRenderer'), None)
            if pr:
                equal(data['renderer'], pr.read_typetree(), label + '.particle renderer')
                materials = [self.source.deref(p, pr.assets_file) for p in pr.read().m_Materials]
                equal([self.asset_source('materials', m) for m in data['materials']], [asset_key(m) for m in materials if m], label + '.particle materials')
                equal(data['source_null_material'], any(m is None for m in materials), label + '.null particle material')
            self.stats['particle_systems'] += 1
        light = next((c for c in components if c.type.name == 'Light'), None)
        equal(node.get('light') is not None, light is not None, label + '.light presence')
        if light:
            equal(node['light'], {'data': light.read_typetree(), 'source': asset_key(light)}, label + '.light')
            self.stats['lights'] += 1
        animator = next((c for c in components if c.type.name == 'Animator'), None)
        equal(node.get('animator') is not None, animator is not None, label + '.animator presence')
        if animator:
            data = animator.read_typetree()
            equal(node['animator']['data'], data, label + '.animator data')
            controller = self.source.deref(data['m_Controller'], animator.assets_file)
            if controller:
                equal(node['animator']['controller'], {'source': asset_key(controller), 'data': controller.read_typetree()}, label + '.controller')
                equal([self.asset_source('animations', clip) for clip in node['animator']['clips']], [asset_key(self.source.deref(p, controller.assets_file)) for p in controller.read_typetree().get('m_AnimationClips', []) if p['m_PathID']], label + '.animation clip sources')
            else:
                equal(node['animator']['controller'], None, label + '.null controller')
            self.stats['animators'] += 1

    def scene(self, doc):
        scene, name = doc['scene'], doc['source']['mission']
        equal(doc['source']['apk_sha256'], self.catalog['apk_sha256'], name + '.apk hash')
        reader = self.source.readers[doc['source']['file'], doc['source']['root_id']]
        rows = self.source_nodes(reader)
        equal(scene['source_mission_node_count'], len(rows), name + '.source count')
        source_nodes = scene['nodes'][:len(rows)]
        equal(len(source_nodes), len(rows), name + '.nodes')
        equal([n['id'] for n in source_nodes], [asset_key(r[0]) for r in rows], name + '.hierarchy set/order')
        transforms = {asset_key(row[2]): i for i, row in enumerate(rows)}
        for i, (node, row) in enumerate(zip(source_nodes, rows)):
            self.node(node, row, source_nodes, scene['prefabs'], transforms, f'{name}.nodes[{i}]')
        renderer_records = {r['id']: r for n in source_nodes for r in n['renderers']}
        expected_waterfalls = {}
        for _, components, _, _ in rows:
            for component in components:
                if component.type.name == 'MonoBehaviour' and self.source.script(component).m_ClassName == 'WaterfallController':
                    data = self.source.data(component)
                    self.stats['waterfall_controllers'] += 1
                    for field, speed in [('_waterR', '_waterSpeed'), ('_borderRR', '_borderSpeed'), ('_borderRL', '_borderSpeed')]:
                        key = asset_key(self.source.deref(data[field], component.assets_file))
                        expected_waterfalls[key] = {'role': 'water' if field == '_waterR' else 'border', 'scroll_speed': data[speed]}
        equal({k: v['waterfall'] for k, v in renderer_records.items() if 'waterfall' in v}, expected_waterfalls, name + '.waterfall source edges')
        self.stats['waterfall_layers'] += len(expected_waterfalls)
        if name.startswith('Mission_'):
            self.stats['levels'] += 1
            self.stats['mission_nodes'] += len(rows)
            self.stats['mission_renderers'] += len(renderer_records)
            background = self.backgrounds[scene['background_key']]
            source_binding=self.background_source.binding(name,background['scene'])
            equal(scene['background_binding'],source_binding,name+'.source background binding')
            equal(scene['background_key'],source_binding['source_mission']['data']['_backgroundPrefabName'],name+'.source background prefab association')
            self.stats['background_bindings'] += 1
            embedded = copy.deepcopy(background['scene']['nodes'])
            offset = len(rows)
            for n in embedded:
                n['background'] = True
                if n['parent'] >= 0:
                    n['parent'] += offset
                for r in n['renderers']:
                    if r.get('bones'):
                        r['bones'] = [b + offset for b in r['bones']]
            equal(scene['nodes'][offset:], embedded, name + '.embedded background')
            for key, prefab in background['scene']['prefabs'].items():
                equal(scene['prefabs'][key], prefab, name + '.background prefab/' + key)
            self.stats['embedded_nodes'] += len(scene['nodes'])
            self.stats['embedded_renderers'] += sum(len(n['renderers']) for n in scene['nodes'])
        else:
            equal(len(scene['nodes']), len(rows), name + '.standalone count')
            self.stats['background_source_nodes'] += len(rows)
            self.stats['background_source_renderers'] += len(renderer_records)
        self.stats['nodes'] += len(rows)

    def negatives(self):
        level = self.load(self.catalog['levels'][0]['path'])
        node = next(n for n in level['scene']['nodes'] if n['scripts'])
        node['scripts'].append(copy.deepcopy(node['scripts'][0]))
        checks = [lambda: self.scene(level)]
        mesh_key = next(k for (kind, k), v in self.assets.items() if kind == 'meshes' and v['source_uv'])
        mesh = copy.deepcopy(self.assets['meshes', mesh_key]); mesh['source_uv'][0] += .125
        checks.append(lambda: self.mesh(self.reader(self.asset_source('meshes', mesh_key)), mesh, 'negative uv'))
        mat_key = next(k for (kind, k), v in self.assets.items() if kind == 'materials' and v['floats'])
        material = copy.deepcopy(self.assets['materials', mat_key]); prop = next(iter(material['floats'])); material['floats'][prop] = 12345.0
        checks.append(lambda: self.material(self.reader(self.asset_source('materials', mat_key)), material, 'negative material'))
        saved = copy.deepcopy(self.stats)
        failures = 0
        for check in checks:
            try:
                check()
            except AssertionError:
                failures += 1
            else:
                raise AssertionError('A negative control escaped the source audit')
        self.stats = saved
        self.stats['negative_controls'] = failures

    def run(self):
        self.verify_assets()
        mission_roots = {(r.read().m_Name, r.assets_file.name, r.path_id) for r in self.source.readers.values() if r.type.name == 'GameObject' and r.read().m_Name.startswith('Mission_')}
        documents = [self.load(l['path']) for l in self.catalog['levels']]
        for doc in documents:
            equal(self.path(next(l['path'] for l in self.catalog['levels'] if l['id'] == doc['id'])).stat().st_size < 16 * 1024 * 1024, True, doc['source']['mission'] + '.document size admission')
        equal(sorted((d['source']['mission'], d['source']['file'], d['source']['root_id']) for d in documents), sorted(mission_roots), 'APK mission roots')
        for name, level in self.catalog['backgrounds'].items():
            self.backgrounds[name] = self.load(level['path'])
            self.scene(self.backgrounds[name])
        for doc in documents:
            self.scene(doc)
        for key, value in self.catalog['counts'].items():
            if key in self.stats:
                equal(self.stats[key], value, 'catalog count/' + key)
        for key, value in [('levels', 17), ('mission_nodes', 9632), ('mission_renderers', 4011), ('anima_resolved', 326), ('fluid_objects', 33), ('waterfall_controllers', 2), ('waterfall_layers', 6), ('source_empty_renderers', 3)]:
            equal(self.stats[key], value, 'independent source invariant/' + key)
        equal(self.catalog['warnings'], [], 'unresolved export warnings')
        self.negatives()
        return dict(self.stats)

    def geometry_reference(self):
        """An independent SciPy quaternion/NumPy skin arithmetic oracle, not pixels."""
        import numpy as np
        from scipy.spatial.transform import Rotation
        result = {'apk_sha256': self.catalog['apk_sha256'], 'boundary': 'original TRS/mesh arithmetic; no dynamic/frame equivalence', 'levels': {}}
        for level in self.catalog['levels']:
            doc = self.load(level['path']); scene = doc['scene']; matrices = []
            for node in scene['nodes']:
                reader = self.reader(node['id']); go = reader.read()
                tr = next(self.source.deref(p, reader.assets_file) for p in go.m_Components if self.source.deref(p, reader.assets_file).type.name in ('Transform', 'RectTransform')).read()
                local = np.eye(4)
                local[:3, :3] = Rotation.from_quat(vec(tr.m_LocalRotation, 'xyzw')).as_matrix() @ np.diag(vec(tr.m_LocalScale, 'xyz'))
                local[:3, 3] = vec(tr.m_LocalPosition, 'xyz')
                matrices.append(local if node['parent'] < 0 else matrices[node['parent']] @ local)
            samples = []
            for index, node in enumerate(scene['nodes']):
                for renderer in node['renderers']:
                    key = scene['prefabs'][renderer['prefab']]['mesh']
                    if not key: continue
                    mesh = self.assets['meshes', key]; count = len(mesh['vertices']) // 3
                    skin = renderer.get('bones') and mesh.get('weights') and mesh.get('bindposes')
                    for vertex in sorted({0, count // 2, count - 1}):
                        p = np.array(mesh['vertices'][vertex * 3:vertex * 3 + 3] + [1.0])
                        if renderer.get('flip_x'): p[0] *= -1
                        if renderer.get('flip_y'): p[1] *= -1
                        if skin:
                            world = np.zeros(3)
                            for slot in range(4):
                                weight = mesh['weights'][vertex * 4 + slot]
                                if not weight: continue
                                bone = mesh['bone_indices'][vertex * 4 + slot]
                                bindpose = np.array(mesh['bindposes'][bone]).reshape(4, 4, order='F')
                                world += (matrices[renderer['bones'][bone]] @ bindpose @ p)[:3] * weight
                        else:
                            world = (matrices[index] @ p)[:3]
                        samples.append({'node': index, 'renderer': renderer['id'], 'vertex': vertex, 'p': world.tolist()})
            result['levels'][level['id']] = {'document_sha256': hashlib.sha256(self.path(level['path']).read_bytes()).hexdigest(),
                'worlds': [m.reshape(-1, order='F').tolist() for m in matrices], 'samples': samples}
        return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--apk', required=True); parser.add_argument('--pack', required=True); parser.add_argument('--report', required=True)
    parser.add_argument('--geometry-reference')
    args = parser.parse_args()
    report = {'pass': False, 'boundary': 'serialized source preservation only; no APK dynamic/rendered-frame parity claim'}
    try:
        audit = Audit(args.apk, args.pack)
        report.update(audit.run())
        report['apk_sha256'] = audit.catalog['apk_sha256']
        if args.geometry_reference:
            reference = pathlib.Path(args.geometry_reference); reference.parent.mkdir(parents=True, exist_ok=True)
            reference.write_text(json.dumps(audit.geometry_reference(), separators=(',', ':')) + '\n', encoding='utf-8')
        report['pass'] = True
    except Exception as exc:
        report['first_difference'] = str(exc)
        raise
    finally:
        out = pathlib.Path(args.report); out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        print(json.dumps(report, ensure_ascii=True))


if __name__ == '__main__':
    main()
