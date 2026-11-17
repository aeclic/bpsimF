"""Export complete original RC2 prefab scenes; no BPLE assets or geometry reduction.

Command: python Tools/RC2Levels/export_levels.py --apk SOURCE.apk --inventory inventory.json --out DIR
PASS: all inventory nodes/renderers have resolved original geometry/material/texture;
all source arrays are finite, indices valid, and source hashes/provenance are retained.
Boundary: serialized static pose plus original waterfall parameters; not RC2 gameplay.
Invalidation: source APK / typetree / decoder / sprite packing convention changes.
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
import uuid

import numpy as np
from PIL import Image
from UnityPy.helpers.MeshHelper import MeshHandler
from UnityPy.export.Texture2DConverter import get_image_from_texture2d
from UnityPy.export.SpriteHelper import SpriteSettings, mask_sprite, render_sprite_mesh
from UnityPy.enums import SpritePackingMode, SpritePackingRotation

from apk_source import ApkSource, asset_key
from background_source import BackgroundSource

PACK_ID = '683178f5-4b60-5fd7-8ab4-f885409b48ca'


def vector(value, names):
    return [float(getattr(value, n)) for n in names]


def floats(value):
    return [float(x) for row in value for x in row]


def write_json(path, data):
    def finite_json(value):
        if isinstance(value,float) and not math.isfinite(value):
            return {'source_ieee754_f32':struct.pack('<f',value).hex()}
        if isinstance(value,dict):return {k:finite_json(v) for k,v in value.items()}
        if isinstance(value,(list,tuple)):return [finite_json(v) for v in value]
        return value
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(finite_json(data), ensure_ascii=False, separators=(',', ':'), allow_nan=False), encoding='utf-8')


def matrix(p, q, s):
    x, y, z, w = q
    length = sum(v*v for v in q)
    if not length:
        raise ValueError('Zero source quaternion')
    f = 2 / length
    r = np.array([[1-f*(y*y+z*z), f*(x*y-z*w), f*(x*z+y*w)],
                  [f*(x*y+z*w), 1-f*(x*x+z*z), f*(y*z-x*w)],
                  [f*(x*z-y*w), f*(y*z+x*w), 1-f*(x*x+y*y)]])
    m = np.eye(4)
    m[:3,:3] = r @ np.diag(s)
    m[:3,3] = p
    return m


class Exporter:
    def __init__(self, source, out):
        self.source, self.out = source, pathlib.Path(out)
        self.assets = {'meshes': {}, 'textures': {}, 'materials': {}, 'fonts': {}, 'animations': {}}
        self.mesh_data = {}
        self.sprites = {}
        self.counts = collections.Counter()
        self.warnings = []
        self.font_data = {}
        self.background_scenes = {}
        self.background_source = BackgroundSource(source)
        self.atlas_by_key = {}
        self.shader_by_name = {}
        for reader in source.readers.values():
            if reader.type.name=='SpriteAtlas':
                for key, value in reader.read().m_RenderDataMap:
                    self.atlas_by_key[repr(key)] = (reader,value)
            if reader.type.name=='Shader':
                data=reader.read()
                name=getattr(getattr(data,'m_ParsedForm',None),'m_Name',None)
                if name:
                    self.shader_by_name[name]=reader

    def save_asset(self, kind, name, data, source_reader):
        path = self.out / 'Assets' / kind / (name + '.json')
        write_json(path, data)
        self.assets[kind][name] = {'path': 'Assets/' + kind + '/' + name + '.json',
                                  'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                                  'source': asset_key(source_reader),
                                  'source_raw_sha256': hashlib.sha256(source_reader.get_raw_data()).hexdigest()}
        return name

    def texture(self, reader, sprite=False, image=None):
        name = ('sprite_' if sprite else 'tex_') + asset_key(reader)
        if name in self.assets['textures']:
            return name
        data = reader.read()
        image = (image if image is not None else data.image).convert('RGBA')
        path = self.out / 'Assets' / 'textures' / (name + '.png')
        path.parent.mkdir(parents=True, exist_ok=True)
        image.save(path)
        self.assets['textures'][name] = {'path': 'Assets/textures/' + name + '.png',
                                       'width': image.width, 'height': image.height,
                                       'rgba_sha256': hashlib.sha256(image.tobytes()).hexdigest(),
                                       'source': asset_key(reader),
                                       'source_raw_sha256': hashlib.sha256(reader.get_raw_data()).hexdigest(),
                                       'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                                       'sprite': sprite, 'srgb': True}
        self.counts['textures'] += 1
        return name

    def sprite(self, reader):
        k = asset_key(reader)
        if k in self.sprites:
            return self.sprites[k]
        sprite = reader.read()
        atlas = self.source.deref(sprite.m_SpriteAtlas, reader.assets_file)
        rd = next(v for key, v in atlas.read().m_RenderDataMap if key == sprite.m_RenderDataKey) if atlas else sprite.m_RD
        if not rd.texture.m_PathID:
            atlas_record = self.atlas_by_key.get(repr(sprite.m_RenderDataKey))
            if atlas_record:
                atlas,rd=atlas_record
        tex = self.source.deref(rd.texture, atlas.assets_file if atlas else reader.assets_file)
        alpha = self.source.deref(rd.alphaTexture, atlas.assets_file if atlas else reader.assets_file)
        if tex is None:
            raise ValueError('No source atlas texture for sprite '+sprite.m_Name+' '+asset_key(reader))
        original=get_image_from_texture2d(tex.read(),False).convert('RGBA')
        if alpha:
            alpha_image=get_image_from_texture2d(alpha.read(),False)
            original.putalpha(alpha_image.split()[0])
        rect=rd.textureRect
        image=original.crop((rect.x,rect.y,rect.x+rect.width,rect.y+rect.height))
        settings=SpriteSettings(rd.settingsRaw)
        if settings.packed:
            transforms={SpritePackingRotation.kSPRFlipHorizontal:Image.Transpose.FLIP_LEFT_RIGHT,
                        SpritePackingRotation.kSPRFlipVertical:Image.Transpose.FLIP_TOP_BOTTOM,
                        SpritePackingRotation.kSPRRotate180:Image.Transpose.ROTATE_180,
                        SpritePackingRotation.kSPRRotate90:Image.Transpose.ROTATE_270}
            if settings.packingRotation in transforms:
                image=image.transpose(transforms[settings.packingRotation])
        if settings.packingMode==SpritePackingMode.kSPMTight:
            h=MeshHandler(sprite.m_RD,reader.assets_file.version)
            h.process()
            image=render_sprite_mesh(sprite,h,original) if h.m_UV0 and any(u or v for u,v in h.m_UV0) else mask_sprite(sprite,h,image)
        image=image.transpose(Image.Transpose.FLIP_TOP_BOTTOM)
        texture = self.texture(reader, sprite=True, image=image)
        td = self.assets['textures'][texture]
        td['source_atlas_texture'] = asset_key(tex)
        td['source_alpha_texture'] = asset_key(alpha) if alpha else None
        td['packing'] = rd.settingsRaw
        offset = vector(rd.textureRectOffset, 'xy')
        result = {'texture': texture, 'name': sprite.m_Name, 'ppu': sprite.m_PixelsToUnits,
                  'rect': [sprite.m_Rect.x, sprite.m_Rect.y, sprite.m_Rect.width, sprite.m_Rect.height],
                  'pivot': vector(sprite.m_Pivot, 'xy'), 'trim_offset': offset,
                  'image_size': [td['width'], td['height']], 'border': vector(sprite.m_Border, 'xyzw'),
                  'sprite': sprite, 'reader': reader}
        self.sprites[k] = result
        return result

    def mesh(self, reader, sprite=None, sprite_geometry=False):
        name = ('spritegeo_' if sprite_geometry else 'mesh_') + asset_key(reader)
        if sprite:
            name += '_sprite_' + asset_key(sprite['reader'])
        if name in self.mesh_data:
            return name
        data = reader.read()
        helper = MeshHandler(data.m_RD if sprite_geometry else data, reader.assets_file.version)
        helper.process()
        vertices = floats(helper.m_Vertices)
        uv = floats(helper.m_UV0 or [(0,0)] * (len(vertices)//3))
        source_uv = list(uv)
        colors = floats(helper.m_Colors or [(1,1,1,1)] * (len(vertices)//3))
        submeshes = [[int(i) for triangle in submesh for i in triangle] for submesh in helper.get_triangles()]
        if sprite:
            # Use an exact unrotated PNG cutout of the source atlas. UVs are an affine
            # coordinate change only; the source positions/triangles and UVs are retained.
            px, py = sprite['pivot']
            _, _, rw, rh = sprite['rect']
            ox, oy = sprite['trim_offset']
            iw, ih = sprite['image_size']
            ppu = sprite['ppu']
            uv = [c for i in range(0,len(vertices),3) for c in
                  ((vertices[i]*ppu + rw*px - ox)/iw, (vertices[i+1]*ppu + rh*py - oy)/ih)]
        count = len(vertices)//3
        if any(not math.isfinite(v) for v in vertices + uv + colors):
            raise ValueError('Non-finite mesh: ' + name)
        if len(uv) != count*2 or len(colors) != count*4:
            raise ValueError('Mismatched vertex attributes: ' + name)
        for triangles in submeshes:
            if len(triangles)%3 or any(i<0 or i>=count for i in triangles):
                raise ValueError('Invalid source topology: ' + name)
        mesh = {'name': getattr(data,'m_Name',name), 'vertices': vertices, 'uv': uv,
                'source_uv': source_uv, 'colors': colors, 'submeshes': submeshes}
        if not sprite_geometry and data.m_BindPose and helper.m_BoneWeights:
            mesh['bindposes'] = [[float(getattr(m, f'e{r}{c}')) for c in range(4) for r in range(4)] for m in data.m_BindPose]
            mesh['weights'] = floats(helper.m_BoneWeights)
            mesh['bone_indices'] = [int(i) for row in helper.m_BoneIndices for i in row]
            if len(mesh['weights']) != count*4 or len(mesh['bone_indices']) != count*4:
                raise ValueError('Incomplete source skin: ' + name)
        mesh['source_vertices_f32_sha256'] = hashlib.sha256(struct.pack('<'+str(len(vertices))+'f', *vertices)).hexdigest()
        self.mesh_data[name] = mesh
        self.counts['meshes'] += 1
        self.counts['vertices'] += count
        self.save_asset('meshes', name, mesh, reader)
        return name

    def material(self, reader):
        if reader is None:
            raise ValueError('Unresolved material')
        name = 'mat_' + asset_key(reader)
        if name in self.assets['materials']:
            return name
        data = reader.read()
        shader = None
        builtin_shader = None
        shader_pointer = data.m_Shader
        if shader_pointer.m_PathID:
            external = reader.assets_file.externals[shader_pointer.m_FileID-1].path if shader_pointer.m_FileID else ''
            if external.endswith('unity_builtin_extra'):
                builtin_shader = shader_pointer.m_PathID
            else:
                shader = self.source.deref(shader_pointer, reader.assets_file)
        shader_name = None
        if shader:
            sd = shader.read()
            shader_name = getattr(getattr(sd,'m_ParsedForm',None),'m_Name',None) or getattr(sd,'m_Name',None)
        if not shader_name and data.m_Name in ('Sprites-Default','BackgroundSpriteMaterial'):
            shader_name = 'Sprites/Default'
            shader = self.shader_by_name.get(shader_name)
        properties = data.m_SavedProperties
        textures = {}
        for slot, env in properties.m_TexEnvs:
            texture_reader = self.source.deref(env.m_Texture, reader.assets_file)
            textures[slot] = {'asset': self.texture(texture_reader) if texture_reader else None,
                              'scale': vector(env.m_Scale,'xy'), 'offset': vector(env.m_Offset,'xy')}
        material = {'name': data.m_Name, 'shader': shader_name, 'builtin_shader_id': builtin_shader, 'queue': data.m_CustomRenderQueue,
                    'textures': textures, 'floats': {k:float(v) for k,v in properties.m_Floats},
                    'colors': {k:vector(v,'rgba') for k,v in properties.m_Colors}}
        if shader:
            material['source_shader']=asset_key(shader)
            material['source_shader_sha256']=hashlib.sha256(shader.get_raw_data()).hexdigest()
            material['source_shader_properties']=shader.read_typetree()['m_ParsedForm']['m_PropInfo']['m_Props']
        self.save_asset('materials',name,material,reader)
        self.counts['materials'] += 1
        return name

    def font(self, reader):
        name = 'font_' + asset_key(reader)
        if name in self.font_data:
            return name
        data = self.source.data(reader)
        font = {'name': data['m_Name'], 'data': data, 'textures': [], 'fallbacks': []}
        self.font_data[name] = font
        texture_pointers = data.get('m_AtlasTextures') or ([data['atlas']] if data.get('atlas',{}).get('m_PathID') else [])
        font['textures'] = [self.texture(self.source.deref(p,reader.assets_file)) for p in texture_pointers]
        font['material'] = self.material(self.source.deref(data['material'],reader.assets_file))
        for pointer in data.get('m_FallbackFontAssetTable') or data.get('fallbackFontAssets',[]):
            fallback = self.source.deref(pointer,reader.assets_file)
            if fallback:
                font['fallbacks'].append(self.font(fallback))
        self.save_asset('fonts',name,font,reader)
        self.counts['fonts'] += 1
        return name

    def export_scene(self, mission, apk_hash):
        source = self.source
        nodes, components, transform_nodes = [], [], {}

        def visit(reader, parent):
            go = reader.read()
            index = len(nodes)
            comps = [source.deref(p,reader.assets_file) for p in go.m_Components]
            tr = next(o for o in comps if o.type.name in ('Transform','RectTransform'))
            t = tr.read()
            node = {'id': asset_key(reader), 'name': go.m_Name, 'parent': parent,
                    'active': bool(go.m_IsActive), 'p': vector(t.m_LocalPosition,'xyz'),
                    'q': vector(t.m_LocalRotation,'xyzw'), 's': vector(t.m_LocalScale,'xyz'),
                    'layer': go.m_Layer, 'renderers': [], 'colliders': [], 'scripts': []}
            node['source_components'] = []
            node['source_game_object'] = {'data': reader.read_typetree(check_read=True),
                'raw_base64': base64.b64encode(reader.get_raw_data()).decode('ascii')}
            for component in comps:
                if component.type.name == 'MonoBehaviour':
                    continue
                node['source_components'].append({'source': asset_key(component), 'type': component.type.name,
                    'data': component.read_typetree(check_read=True),
                    'raw_base64': base64.b64encode(component.get_raw_data()).decode('ascii')})
                self.counts['native_components'] += 1
            if tr.type.name=='RectTransform':
                node['rect']={'size':vector(t.m_SizeDelta,'xy'),'pivot':vector(t.m_Pivot,'xy'),
                              'anchor_min':vector(t.m_AnchorMin,'xy'),'anchor_max':vector(t.m_AnchorMax,'xy')}
            nodes.append(node)
            components.append(comps)
            transform_nodes[asset_key(tr)] = index
            for p in t.m_Children:
                child = source.deref(p,tr.assets_file)
                visit(source.deref(child.read().m_GameObject,child.assets_file),index)

        visit(source.readers[(mission['file'],mission['id'])],-1)
        prefabs = {}
        for index, (node, comps) in enumerate(zip(nodes,components)):
            anima = None
            for c in comps:
                if c.type.name != 'MonoBehaviour':
                    continue
                script = source.script(c)
                if script is None:
                    continue
                typ = script.m_ClassName
                record = {'type': typ, 'namespace': script.m_Namespace, 'source': asset_key(c),
                          'raw_base64': base64.b64encode(c.get_raw_data()).decode('ascii')}
                if typ in ('SpriteMeshInstance','Bone2D','WaterfallController','TrackObjectsController','TextMeshPro','TMP_SubMesh','FluidObject','BackgroundContainer','BackgroundParallax'):
                    record['data'] = source.data(c)
                if typ == 'SpriteMeshInstance':
                    anima = (c,record['data'])
                if typ == 'TextMeshPro':
                    text_data=record['data']
                    node['text']={'data':text_data,
                                  'font':self.font(source.deref(text_data['m_fontAsset'],c.assets_file)),
                                  'material':self.material(source.deref(text_data['m_sharedMaterial'],c.assets_file))}
                    self.counts['text_objects'] += 1
                if typ == 'TMP_SubMesh':
                    subfont=source.deref(record['data']['m_fontAsset'],c.assets_file)
                    if subfont:
                        node['text_submesh_font']=self.font(subfont)
                if typ == 'FluidObject':
                    node['fluid']={'data':record['data'],'source_generated_mesh':True}
                    self.counts['fluid_objects'] += 1
                node['scripts'].append(record)
                self.counts['scripts'] += 1
            for c in comps:
                if c.type.name=='ParticleSystem':
                    node['particles']={'data':c.read_typetree(),'source':asset_key(c),'materials':[]}
                    self.counts['particle_systems'] += 1
                elif c.type.name=='Light':
                    node['light']={'data':c.read_typetree(),'source':asset_key(c)}
                elif c.type.name=='Animator':
                    animator=c.read_typetree()
                    controller=source.deref(animator['m_Controller'],c.assets_file)
                    node['animator']={'data':animator,'controller':None,'clips':[]}
                    if controller:
                        controller_data=controller.read_typetree()
                        node['animator']['controller']={'source':asset_key(controller),'data':controller_data}
                        for pointer in controller_data.get('m_AnimationClips',[]):
                            clip=source.deref(pointer,controller.assets_file)
                            if clip:
                                name='clip_'+asset_key(clip)
                                if name not in self.assets['animations']:self.save_asset('animations',name,clip.read_typetree(),clip)
                                node['animator']['clips'].append(name)
            for c in comps:
                if c.type.name=='ParticleSystemRenderer' and node.get('particles'):
                    pr=c.read()
                    node['particles']['renderer']=c.read_typetree()
                    materials=[source.deref(p,c.assets_file) for p in pr.m_Materials]
                    node['particles']['materials']=[self.material(m) for m in materials if m is not None]
                    node['particles']['source_null_material']=any(m is None for m in materials)
            for c in comps:
                typ = c.type.name
                if 'Collider' in typ:
                    node['colliders'].append({'type': typ, 'data': c.read_typetree()})
                    continue
                if typ not in ('SpriteRenderer','MeshRenderer','SkinnedMeshRenderer'):
                    continue
                renderer = c.read()
                sprite = None
                mesh = None
                smi = None
                if typ == 'SpriteRenderer':
                    sprite_reader = source.deref(renderer.m_Sprite,c.assets_file)
                    if sprite_reader:
                        sprite = self.sprite(sprite_reader)
                        mesh = self.mesh(sprite_reader,sprite,sprite_geometry=True)
                elif anima:
                    smi_reader,smi = anima
                    sm_reader = source.deref(smi['m_SpriteMesh'],smi_reader.assets_file)
                    sm_data = source.data(sm_reader)
                    mr = source.deref(sm_data['m_SharedMesh'],sm_reader.assets_file)
                    sprite = self.sprite(source.deref(sm_data['m_Sprite'],sm_reader.assets_file))
                    mesh = self.mesh(mr,sprite)
                    self.counts['anima_resolved'] += 1
                elif typ == 'SkinnedMeshRenderer':
                    mr = source.deref(renderer.m_Mesh,c.assets_file)
                    if mr:
                        mesh = self.mesh(mr)
                else:
                    mf = next((o for o in comps if o.type.name=='MeshFilter'),None)
                    mr = source.deref(mf.read().m_Mesh,mf.assets_file) if mf else None
                    if mr:
                        mesh = self.mesh(mr)
                text_generated = bool(node.get('text')) or any(v['type']=='TMP_SubMesh' for v in node['scripts'])
                source_empty = mesh is None and not text_generated and not node.get('fluid')
                if source_empty and not (typ == 'SpriteRenderer' and not renderer.m_Sprite.m_PathID and anima is None):
                    # A renderer is only visually empty when the source has no sprite/
                    # mesh AND no dynamic SpriteMeshInstance or text component supplies it.
                    self.warnings.append({'mission':mission['name'],'node':node['id'],'renderer':typ,'reason':'source renderer has no serialized geometry'})
                material_pointers = smi['m_Materials'] if smi else renderer.m_Materials
                material_file = anima[0].assets_file if smi else c.assets_file
                material_ids = []
                for pointer in material_pointers:
                    mat = source.deref(pointer,material_file)
                    if mat is None:
                        if text_generated and mesh is None:
                            continue
                        raise ValueError('Null source material: '+mission['name']+'/'+node['name']+'/'+typ+' anima='+str(bool(smi)))
                    material_ids.append(self.material(mat))
                prefab_data = {'kind':typ, 'mesh':mesh, 'materials':material_ids,
                               'texture':sprite['texture'] if sprite else None,'text_generated':text_generated,
                               'fluid_generated':bool(node.get('fluid'))}
                if sprite:
                    prefab_data['sprite'] = {k:sprite[k] for k in ['name','ppu','rect','pivot','trim_offset','image_size','border']}
                prefab_id = hashlib.sha256(json.dumps(prefab_data,sort_keys=True).encode()).hexdigest()[:24]
                prefabs[prefab_id] = prefab_data
                r = {'id':asset_key(c),'prefab':prefab_id,'enabled':bool(renderer.m_Enabled),
                     'sorting_layer':renderer.m_SortingLayer,'sorting_layer_id':renderer.m_SortingLayerID,
                     'sorting_order':renderer.m_SortingOrder,'color':vector(renderer.m_Color,'rgba') if typ=='SpriteRenderer' else [1,1,1,1]}
                if source_empty:
                    r['source_empty'] = True
                    self.counts['source_empty_renderers'] += 1
                if typ=='SpriteRenderer':
                    r.update({'flip_x':renderer.m_FlipX,'flip_y':renderer.m_FlipY,
                              'draw_mode':renderer.m_DrawMode,'size':vector(renderer.m_Size,'xy')})
                    self.counts['sprite_draw_mode_'+str(renderer.m_DrawMode)] += 1
                if typ=='SkinnedMeshRenderer' or smi:
                    pointers = smi['m_BoneTransforms'] if smi else renderer.m_Bones
                    file = anima[0].assets_file if smi else c.assets_file
                    r['bones'] = [transform_nodes[asset_key(source.deref(p,file))] for p in pointers]
                    if smi:
                        r['color'] = [smi['m_Color'][k] for k in 'rgba']
                node['renderers'].append(r)
                self.counts['renderers'] += 1
        worlds = []
        for n in nodes:
            local = matrix(n['p'],n['q'],n['s'])
            worlds.append(worlds[n['parent']] @ local if n['parent']>=0 else local)
        renderer_nodes={r['id']:(i,r) for i,n in enumerate(nodes) for r in n['renderers']}
        go_nodes={n['id']:i for i,n in enumerate(nodes)}
        for n in nodes:
            for script in n['scripts']:
                data=script.get('data',{})
                if script['type']=='WaterfallController':
                    self.counts['waterfall_controllers'] += 1
                    file=source.readers[(mission['file'],int(script['source'].rsplit('_',1)[1]))].assets_file
                    for field,speed in [('_waterR','_waterSpeed'),('_borderRR','_borderSpeed'),('_borderRL','_borderSpeed')]:
                        reader=source.deref(data[field],file)
                        _,r=renderer_nodes[asset_key(reader)]
                        r['waterfall']={'role':'water' if field=='_waterR' else 'border','scroll_speed':data[speed]}
                        self.counts['waterfall_layers'] += 1
                if script['type']=='TrackObjectsController':
                    file=source.readers[(mission['file'],int(script['source'].rsplit('_',1)[1]))].assets_file
                    for pointer in data.get('_gameObjects',[])+data.get('_foregroundObjects',[]):
                        reader=source.deref(pointer,file)
                        if reader and asset_key(reader) in go_nodes:
                            nodes[go_nodes[asset_key(reader)]]['runtime_active']=True
        bounds = []
        for index,node in enumerate(nodes):
            for r in node['renderers']:
                mid = prefabs[r['prefab']]['mesh']
                if mid:
                    v = np.array(self.mesh_data[mid]['vertices']).reshape(-1,3)
                    transformed = np.c_[v,np.ones(len(v))] @ worlds[index].T
                    bounds.extend([transformed[:,:3].min(axis=0).tolist(),transformed[:,:3].max(axis=0).tolist()])
        bmin,bmax = np.array(bounds).min(axis=0).tolist(),np.array(bounds).max(axis=0).tolist()
        doc_id = str(uuid.uuid5(uuid.NAMESPACE_URL,'rc2/0.1.2/'+mission['name']))
        terrain_points=[]
        for i,n in enumerate(nodes):
            if any('Ferr2DT_PathTerrain'==s['type'] for s in n['scripts']):
                for r in n['renderers']:
                    mid=prefabs[r['prefab']]['mesh']
                    if mid:
                        v=np.array(self.mesh_data[mid]['vertices']).reshape(-1,3)
                        terrain_points.extend((np.c_[v,np.ones(len(v))] @ worlds[i].T)[:,:3].tolist())
        spawn_x=5.0
        near=[v[1] for v in terrain_points if abs(v[0]-spawn_x)<2]
        spawn=[spawn_x,max(near)+2 if near else 3.0,0]
        mission_data=self.background_source.missions.get(mission['name'])
        background_key=mission_data['data']['_backgroundPrefabName'] if mission_data else None
        mission_node_count=len(nodes)
        mission_renderer_count=sum(len(n['renderers']) for n in nodes)
        if mission['name'].startswith('Mission_') and background_key in self.background_scenes:
            background=self.background_scenes[background_key]
            offset=len(nodes)
            for node in copy.deepcopy(background['nodes']):
                node['background']=True
                if node['parent']>=0:node['parent']+=offset
                for renderer in node['renderers']:
                    if renderer.get('bones'):renderer['bones']=[b+offset for b in renderer['bones']]
                nodes.append(node)
            prefabs.update(copy.deepcopy(background['prefabs']))
        doc = {'format':'rc2.level','version':1,'id':doc_id,'title':'RC2 0.1.2 · '+mission['name'],
               'pack_id':PACK_ID,'source':{'apk_sha256':apk_hash,'unity':source.version,
               'mission':mission['name'],'file':mission['file'],'root_id':mission['id']},
               'scene':{'nodes':nodes,'prefabs':prefabs,'unit_scale':0.25,'bounds':[bmin,bmax],
                        'spawn':spawn,'mesh_overrides':{},'animations_enabled':True,
                        'background_key':background_key,'source_mission_node_count':mission_node_count}}
        doc['scene']['source_components_runtime'] = 'archive_only; operational edits use nodes/renderers/colliders/particles/text'
        if mission_data:
            doc['scene']['background_binding']=self.background_source.binding(mission['name'],self.background_scenes[background_key])
        self.counts['nodes'] += mission_node_count
        if mission_node_count != mission['node_count']:
            raise ValueError('Inventory node mismatch: '+mission['name'])
        path = 'Levels/'+doc_id.replace('-','')+'.rc2level'
        write_json(self.out/path,doc)
        return {'id':doc_id,'title':doc['title'],'mission':mission['name'],'path':path,
                'nodes':len(nodes),'renderers':sum(len(n['renderers']) for n in nodes),
                'source_nodes':mission_node_count,'source_renderers':mission_renderer_count,
                'background_binding':doc['scene'].get('background_binding'),
                'bounds':[bmin,bmax]}


def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--apk',required=True)
    ap.add_argument('--inventory',required=True)
    ap.add_argument('--out',required=True)
    args=ap.parse_args()
    inventory=json.loads(pathlib.Path(args.inventory).read_text(encoding='utf-8'))
    apk_hash=hashlib.sha256(pathlib.Path(args.apk).read_bytes()).hexdigest()
    if apk_hash != inventory['apk_sha256']:
        raise ValueError('APK differs from inventory oracle')
    exporter=Exporter(ApkSource(args.apk,generate_trees=True),args.out)
    backgrounds={}
    for reader in exporter.source.readers.values():
        if reader.type.name!='GameObject':continue
        name=reader.read().m_Name
        if name not in ('background_test','background_sand'):continue
        def descendants(go):
            data=go.read();components=[exporter.source.deref(p,go.assets_file) for p in data.m_Components]
            tr=next(c for c in components if c.type.name=='Transform');count=1
            for pointer in tr.read().m_Children:
                child=exporter.source.deref(pointer,tr.assets_file);count+=descendants(exporter.source.deref(child.read().m_GameObject,child.assets_file))
            return count
        row={'name':name,'file':reader.assets_file.name,'id':reader.path_id,'node_count':descendants(reader)}
        backgrounds[name]=exporter.export_scene(row,apk_hash)
        background_doc=json.loads((pathlib.Path(args.out)/backgrounds[name]['path']).read_text(encoding='utf-8'))
        exporter.background_scenes[name]=background_doc['scene']
    levels=[]
    for mission in inventory['missions']:
        levels.append(exporter.export_scene(mission,apk_hash))
        print('EXPORTED',mission['name'],levels[-1]['nodes'],levels[-1]['renderers'],flush=True)
    exporter.counts.update({
        'mission_nodes':sum(l['source_nodes'] for l in levels),
        'mission_renderers':sum(l['source_renderers'] for l in levels),
        'background_source_nodes':sum(l['source_nodes'] for l in backgrounds.values()),
        'background_source_renderers':sum(l['source_renderers'] for l in backgrounds.values()),
        'embedded_nodes':sum(l['nodes'] for l in levels),
        'embedded_renderers':sum(l['renderers'] for l in levels)})
    manifest={'format':'rc2.level-pack','version':1,'id':PACK_ID,'title':'RC2关卡mod',
              'apk_sha256':apk_hash,'levels':levels,'backgrounds':backgrounds,'assets':exporter.assets,'counts':dict(exporter.counts),
              'warnings':exporter.warnings,'source_members':exporter.source.members}
    write_json(pathlib.Path(args.out)/'catalog.json',manifest)
    print(json.dumps({'pass':not exporter.warnings,'counts':dict(exporter.counts),'warnings':exporter.warnings[:20]},ensure_ascii=False))


if __name__=='__main__':
    main()
