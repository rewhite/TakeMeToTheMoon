"""Build the segmented mannequin with Blender 4.x.

Run: blender --background --python build_mannequin.py -- /path/to/output
The JSON specification beside this script uses Unity coordinates, in meters.
"""
import bpy
import json
import math
import sys
from pathlib import Path
from mathutils import Vector, Matrix

BASE = Path(__file__).resolve().parent.parent
SPEC = json.loads((Path(__file__).parent / 'ragdoll_spec.json').read_text())
OUT = Path(sys.argv[sys.argv.index('--') + 1]) if '--' in sys.argv else BASE
MODEL_DIR = OUT / 'Assets' / 'JointedMannequin' / 'Models'
MODEL_DIR.mkdir(parents=True, exist_ok=True)
(OUT/'Source').mkdir(parents=True, exist_ok=True)

def U(v):
    """Unity +Y up/+Z front to Blender +Z up/-Y front."""
    return Vector((v[0], -v[2], v[1]))

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0
scene.render.engine = 'CYCLES'
scene.cycles.samples = 48
scene.cycles.use_denoising = False

materials = {}
for name, rgba in SPEC['materials'].items():
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = rgba
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = rgba
    bsdf.inputs['Roughness'].default_value = .26 if name != 'Visor' else .19
    bsdf.inputs['Metallic'].default_value = .04
    materials[name] = mat

def smooth(obj):
    for face in obj.data.polygons:
        face.use_smooth = True
    return obj

def ellipsoid(name, center, radii, material, segments=24, rings=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, radius=1, location=U(center))
    obj = bpy.context.object
    obj.name = name
    obj.scale = (radii[0],radii[2],radii[1])
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(materials[material])
    return smooth(obj)

def rounded_limb(name, a, b, radius, inset, material='Porcelain'):
    a, b = U(a), U(b)
    direction = (b-a).normalized()
    a, b = a+direction*inset, b-direction*inset
    length = (b-a).length
    cap = min(radius*.50,length*.15)
    # A smooth beveled cylinder with subtly tapered middle, baked into a mesh.
    rings = [(-length/2, radius*.69),(-length/2+cap*.25,radius*.89),(-length/2+cap,radius),
             (0,radius*.97),(length/2-cap,radius*.94),(length/2-cap*.25,radius*.84),(length/2,radius*.65)]
    count=24
    vertices=[]
    for z,r in rings:
        for i in range(count):
            t=i*math.tau/count
            vertices.append((r*math.cos(t),r*math.sin(t),z))
    faces=[]
    for j in range(len(rings)-1):
        for i in range(count):
            a0=j*count+i; a1=j*count+(i+1)%count
            faces.append((a0,a1,a1+count,a0+count))
    faces.extend([tuple(reversed(range(count))),tuple((len(rings)-1)*count+i for i in range(count))])
    mesh=bpy.data.meshes.new(name+'_Geometry')
    mesh.from_pydata(vertices,[],faces)
    mesh.update()
    obj=bpy.data.objects.new(name,mesh)
    scene.collection.objects.link(obj)
    obj.location=(a+b)/2
    obj.rotation_mode='QUATERNION'
    obj.rotation_quaternion=direction.to_track_quat('Z','Y')
    obj.data.materials.append(materials[material])
    return smooth(obj)

def join_part(name, objects, center):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:o.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.object.join()
    obj=bpy.context.object
    obj.name=name
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    scene.cursor.location=U(center)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    obj.data.name=name+'_Mesh'
    obj['body_section']=name
    obj['units']='meters'
    return obj

objects={}
for p in SPEC['parts']:
    name=p['name']; c=p['center']; pieces=[]
    if name=='Pelvis':
        pieces.append(ellipsoid(name,c,[.145,.117,.105],'Porcelain',32,20))
    elif name=='Chest':
        pieces.append(ellipsoid(name,c,[.185,.202,.12],'Porcelain',32,20))
        pieces.append(ellipsoid('Abdomen',[0,1.102,0],[.112,.066,.09],'Porcelain'))
        pieces.append(ellipsoid('WaistJoint',[0,1.085,0],[.061,.056,.058],'Apricot'))
    elif name=='Head':
        pieces.append(ellipsoid(name,c,[.111,.145,.103],'Porcelain',32,24))
        pieces.append(ellipsoid('NeckJoint',p['joint']['anchor'],[.044,.045,.044],'Apricot'))
        # Smooth black visor and two small glints on the forward-facing surface.
        pieces.append(ellipsoid('Visor',[0,1.652,.094],[.076,.026,.018],'Visor',32,16))
        pieces.append(ellipsoid('VisorGlintA',[-.036,1.658,.109],[.013,.004,.003],'Highlight',16,8))
        pieces.append(ellipsoid('VisorGlintB',[.027,1.647,.111],[.007,.003,.0025],'Highlight',12,8))
    elif name.startswith('Hand'):
        pieces.append(ellipsoid(name,c,[.047,.058,.041],'Apricot'))
        pieces.append(ellipsoid('WristJoint',p['joint']['anchor'],[.034,.035,.034],'Apricot'))
    elif name.startswith('Foot'):
        pieces.append(ellipsoid(name,c,[.049,.058,.113],'Porcelain'))
        pieces.append(ellipsoid('Toe',[c[0],.045,.136],[.047,.031,.045],'Apricot'))
        pieces.append(ellipsoid('AnkleJoint',p['joint']['anchor'],[.037,.037,.037],'Apricot'))
    else:
        a,b=p['endpoints']
        r={'UpperArm':.048,'Forearm':.039,'Thigh':.063,'Shin':.047}[name.split('_')[0]]
        jr={'UpperArm':.056,'Forearm':.044,'Thigh':.057,'Shin':.051}[name.split('_')[0]]
        pieces.append(rounded_limb(name,a,b,r,.025))
        pieces.append(ellipsoid(name+'_Joint',p['joint']['anchor'],[jr,jr,jr],'Apricot'))
    objects[name]=join_part(name,pieces,c)

# Export only the 15 actual body sections. No stage, lights, camera, or dummy model.
bpy.ops.object.select_all(action='DESELECT')
for o in objects.values():o.select_set(True)
bpy.context.view_layer.objects.active=objects['Pelvis']
bpy.ops.export_scene.fbx(
    filepath=str(MODEL_DIR/'JointedMannequin.fbx'),use_selection=True,
    object_types={'MESH'},global_scale=1.0,apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',
    use_space_transform=True,bake_space_transform=False,use_mesh_modifiers=True,
    mesh_smooth_type='FACE',use_tspace=False,add_leaf_bones=False,
    bake_anim=False,path_mode='AUTO',use_custom_props=True)

# Keep the editable source file clean, before adding the presentation stage.
scene.cursor.location=(0,0,0)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Source'/'JointedMannequin.blend'))

# Render a real geometry preview; this is not a simulation screenshot.
floor=bpy.data.materials.new('PreviewFloor')
floor.diffuse_color=(.33,.39,.43,1)
floor.use_nodes=True
floor.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.33,.39,.43,1)
floor.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.8
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,0))
bpy.context.object.name='PreviewFloor'
bpy.context.object.data.materials.append(floor)

world=scene.world or bpy.data.worlds.new('World')
scene.world=world
world.use_nodes=True
world.node_tree.nodes['Background'].inputs[0].default_value=(.52,.60,.67,1)
world.node_tree.nodes['Background'].inputs[1].default_value=.35

def area(name,location,power,size,target):
    data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size
    obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj)
    obj.location=location;obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()

area('Key',(-3,-4,5),650,4,(0,0,.9))
area('Fill',(3,-2,2.5),350,3,(0,0,.9))
area('Rim',(1,3,4),850,3,(0,0,1))
camera_data=bpy.data.cameras.new('PreviewCamera')
camera=bpy.data.objects.new('PreviewCamera',camera_data)
scene.collection.objects.link(camera)
camera.location=(2.35,-6,2.3)
camera.rotation_euler=(Vector((0,0,.9))-camera.location).to_track_quat('-Z','Y').to_euler()
camera_data.type='ORTHO';camera_data.ortho_scale=2.17;camera_data.lens=55
scene.camera=camera
scene.render.resolution_x=1000;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.render.film_transparent=False
scene.render.filepath=str(OUT/'MannequinPreview.png')
scene.view_settings.view_transform='AgX'
bpy.ops.render.render(write_still=True)

metrics={'body_sections':len(objects),'vertices':sum(len(o.data.vertices) for o in objects.values()),
         'triangles':sum(sum(len(f.vertices)-2 for f in o.data.polygons) for o in objects.values()),
         'fbx_bytes':(MODEL_DIR/'JointedMannequin.fbx').stat().st_size,
         'body_names':list(objects)}
(OUT/'Source'/'model_metrics.json').write_text(json.dumps(metrics,indent=2)+'\n')
print('MANNEQUIN_BUILD_OK',json.dumps(metrics))
