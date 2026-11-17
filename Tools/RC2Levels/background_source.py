"""Read source mission background names, location edges and parallax camera data.

Consumed by export_levels.py/verify_source.py. No name-based biome guessing: use
GameMissionDataItem._prefabName/_backgroundPrefabName and direct Location lists.
Unlisted missions retain the observed BackgroundParallax scene defaults and mark
location=None; they are not silently assigned a guessed Location from their id.
Scope: serialization and explicit binding contract, not native APK update timing.
"""
import hashlib
from apk_source import asset_key


class BackgroundSource:
    def __init__(self, source):
        self.source=source;self.missions={};self.locations={};self.parallax=None
        locations=[]
        for reader in source.readers.values():
            if reader.type.name!='MonoBehaviour':continue
            script=source.script(reader)
            if script is None:continue
            name=script.m_ClassName
            if name not in ('GameMissionDataItem','GameLocationDataItem','BackgroundParallax'):continue
            data=source.data(reader)
            record={'source':asset_key(reader),'source_raw_sha256':hashlib.sha256(reader.get_raw_data()).hexdigest(),'data':data}
            if name=='GameMissionDataItem':
                key=data['_prefabName']
                if key in self.missions:raise ValueError('Ambiguous source mission prefab '+key)
                self.missions[key]=record
            elif name=='GameLocationDataItem':locations.append((reader,record))
            else:
                if self.parallax is not None:raise ValueError('Ambiguous source BackgroundParallax')
                self.parallax=record
                camera=source.deref(data['_parallaxCamera'],reader.assets_file)
                self.camera={'source':asset_key(camera),'source_raw_sha256':hashlib.sha256(camera.get_raw_data()).hexdigest(),'data':camera.read_typetree(check_read=True)}
                main=source.deref(data['_mainCamera'],reader.assets_file)
                self.main_camera={'source':asset_key(main),'source_raw_sha256':hashlib.sha256(main.get_raw_data()).hexdigest(),'data':main.read_typetree(check_read=True)}
        if self.parallax is None:raise ValueError('Source BackgroundParallax not found')
        for reader,record in locations:
            for field in ('_companyMissions','_deliveryMissions','_farmingMissions'):
                for pointer in record['data'][field]:
                    mission=source.deref(pointer,reader.assets_file)
                    key=asset_key(mission)
                    if key in self.locations:raise ValueError('Ambiguous source mission location '+key)
                    self.locations[key]=record

    def binding(self, mission, background_scene):
        record=self.missions[mission];name=record['data']['_backgroundPrefabName']
        location=self.locations.get(record['source'])
        parameters=location['data'] if location else self.parallax['data']
        nodes=background_scene['nodes']
        by_transform={c['source']:n['id'] for n in nodes for c in n['source_components'] if c['type']=='Transform'}
        controllers=[s for n in nodes for s in n['scripts'] if s['type']=='BackgroundContainer']
        if len(controllers)!=1:raise ValueError('Ambiguous background container '+name)
        controller=controllers[0];data=controller['data'];reader=self.source.readers[(controller['source'].rsplit('_',1)[0],int(controller['source'].rsplit('_',1)[1]))]
        def node(pointer):
            value=self.source.deref(pointer,reader.assets_file)
            return by_transform[asset_key(value)] if value else None
        layers=[]
        for index,pointer in enumerate(data['_layersTransform']):
            layers.append({'node_id':node(pointer),'speed_coeff':parameters['_layersSpeedCoeff'][index],
                'altitude_coeff':parameters['_layersAltitudeCoeff'][index],
                'tile_node_ids':[node(p) for p in data['_parallaxTiles'][index]['Tiles']]})
        roots=[n['id'] for n in nodes if n['parent']<0]
        if len(roots)!=1:raise ValueError('Background root count differs')
        return {'version':1,'enabled':True,'root_id':roots[0],'fixed_node_id':node(data['_bgTransform']),
            'background_key':name,'source_mission':record,'source_location':location,
            'source_parallax':self.parallax,'source_camera':self.camera,'source_main_camera':self.main_camera,'layers':layers,
            'camera_ortho_size':self.camera['data']['orthographic size'],'repeat_width':self.parallax['data']['_bounds']*2,
            'policy':'source_camera_projection; camera-relative layer contract, not APK frame-phase equivalence'}
