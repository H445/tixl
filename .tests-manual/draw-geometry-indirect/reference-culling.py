"""Run in a disposable TiXL feature checkout. Argument: checkout of H445/tixl-culling-lod with submodules."""
import json
import re
import subprocess
import sys
from pathlib import Path

ref = Path(sys.argv[1])
def rev(path):
    return subprocess.check_output(['git','-C',str(path),'rev-parse','HEAD'], text=True).strip()
assert rev(ref) == 'f5d5558db328cf3c7f8a1b8daa82f4022c2bffcc'
component = ref/'components/culling'
assert rev(component) == '3b9a27f8f6f8b278b1220ff6555a012794a98e79'
src = component/'TiXL/Culling'
ex = Path('Operators/Examples/Symbols/Lib/mesh')
assets = Path('Operators/Examples/Assets')
for name in ['DepthCullPass','_DrawFrustumTriangles']:
    for ext in ['.cs','.t3','.t3ui']:
        contents = (src/'Symbols'/(name+ext)).read_text(encoding='utf-8-sig')
        (ex/(name+ext)).write_text(contents.replace('namespace SceneTools.Culling;', 'namespace Examples.Lib.mesh;').replace('SceneTools.Culling:', 'Examples:'),encoding='utf8')
# Verify once during warm-up that the reference actually chose its depth shader.
triangles = ex/'_DrawFrustumTriangles.cs'
cs = triangles.read_text(encoding='utf8').replace('    private bool _cached, _hasBounds;', '    private bool _cached, _hasBounds, _reportedDepth;')
source = 'if (depthShader != null) mark = depthShader;'
assert source in cs
triangles.write_text(cs.replace(source, '''if (depthShader != null)
                    {
                        mark = depthShader;
                        if (!_reportedDepth)
                        {
                            Log.Info("Reference MarkDepth selected on live depth.", this);
                            _reportedDepth = true;
                        }
                    }'''),encoding='utf8')
for filename in ['Frustum triangles.hlsl','Occlusion depth.hlsl']:
    (assets/filename).write_bytes((src/'Assets'/filename).read_bytes())

def graph(p):
    return json.loads(re.sub(r'/\*.*?\*/','',p.read_text(encoding='utf-8-sig'),flags=re.S))
def save(p,d):p.write_text(json.dumps(d,indent=2)+'\n',encoding='utf8')
def change(p,old,new):
    s=p.read_text(encoding='utf-8-sig');assert old in s;p.write_text(s.replace(old,new),encoding='utf8')
lib = Path('Operators/Lib/Symbols/mesh/draw')
name = 'ReferenceCullingUnlit'
uid = '11736bae-b076-4a2b-ae75-fc174d99b719'
original = (lib/'DrawMeshUnlit.cs').read_text(encoding='utf-8-sig')
(ex/(name+'.cs')).write_text(original.replace('namespace Lib.mesh.draw;', 'namespace Examples.Lib.mesh;').replace('DrawMeshUnlit',name).replace('4499dcb1-c936-49ed-861b-2ad8ae58cb28',uid),encoding='utf8')
d=graph(lib/'DrawMeshUnlit.t3');d['Id']=uid
draw=next(v for v in d['Children'] if v['SymbolName']=='Lib.render._dx11.api.Draw')
draw.update(SymbolId='7a9988dd-cd72-5bde-8271-e9286b2e454d',SymbolName='Examples.Lib.mesh._DrawFrustumTriangles',InputValues=[dict(Id='8a59453b-31c6-5829-9ac5-e2b913ba23dc',Type='System.Boolean',Value=True)])
def connection(sp,slot,tp,ts):return dict(SourceParentOrChildId=sp,SourceSlotId=slot,TargetParentOrChildId=tp,TargetSlotId=ts)
zero='00000000-0000-0000-0000-000000000000'
for c in d['Connections']:
    if c['TargetParentOrChildId']==draw['Id']:
        c.update(connection(zero,'be057b0a-1302-4076-bde1-6ae453815642',draw['Id'],'54c1c010-d527-5fd8-a15f-8fed02232b62'))
    if c['SourceParentOrChildId']==draw['Id']: c['SourceSlotId']='5f280164-2577-50f9-8267-486628daee70'
save(ex/(name+'.t3'),d)
ui=graph(lib/'DrawMeshUnlit.t3ui');ui['Id']=uid;save(ex/(name+'.t3ui'),ui)
p=ex/'DrawGeometryIndirectExample.t3';d=graph(p)
field=next(v for v in d['Children'] if v['Id']=='9397b896-825c-5404-aa41-0c9335117d3b')
field.update(SymbolId=uid,SymbolName='Examples.Lib.mesh.'+name)
depth='1bf06093-ab64-4140-944b-f9332341cdd3'
d['Children'].append(dict(Id=depth,SymbolId='8022279f-6259-5491-b212-9a6f84780944',SymbolName='Examples.Lib.mesh.DepthCullPass',InputValues=[],Outputs=[]))
for c in d['Connections']:
    if c['SourceParentOrChildId']==field['Id']:c.update(SourceParentOrChildId=depth,SourceSlotId='6a81fee5-fce2-5b5c-a882-e4a2bfbf8ac6')
d['Connections'].append(connection(field['Id'],'0e5c4ba6-278c-4c3c-96d8-00b706c5605b',depth,'85dc5b6a-fe05-5606-9c2f-fd1f20c90b83'))
save(p,d)
change(ex/'_IndirectOrbitScene.cs','mode == 0 ? 0 : 1','mode is 0 or 2 ? 0 : 1')
change(ex/'_MeasureIndirectOrbit.cs','ExampleModes = [6, 7, 3, 4]', 'ExampleModes = [6, 7, 3, 4, 2]')
