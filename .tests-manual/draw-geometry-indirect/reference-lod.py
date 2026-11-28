"""Extend the pinned culling harness with the reference project's occupied LOD tiers."""
import runpy
import uuid

globals().update({k:v for k,v in runpy.run_path(str(__import__('pathlib').Path(__file__).with_name('reference-culling.py'))).items() if not k.startswith('__')})
root = ref/'TiXL'
def key(name):
    return str(uuid.uuid5(uuid.NAMESPACE_URL,'tixl/ring-reference/'+name))
def contents(p):
    return p.read_text(encoding='utf-8-sig')
def relocate(s):
    return s.replace('SceneTools.CameraVisibility','Examples.Lib.mesh').replace('SceneTools.Culling:', 'Examples:').replace('SceneTools.Culling.', 'Examples.Lib.mesh.').replace('namespace SceneTools.Culling;', 'namespace Examples.Lib.mesh;')
for pkg,names in [('CameraVisibility',['SplitPointsByLod','PointListToGpuBuffer']),('Culling',['DrawCulledCopies','_DrawClusterCopies'])]:
    for n in names:
        for ext in ['.cs','.t3','.t3ui']:
            (ex/(n+ext)).write_text(relocate(contents(root/pkg/'Symbols'/(n+ext))),encoding='utf8')
for n in ['PointLodSelection','CameraVolume','XPanelOcclusion','LodProjection']:
    p=root/'CameraVisibility/Utils'/(n+'.cs')
    if not p.exists():p=ref/'components/lod/TiXL/CameraVisibility/Utils'/(n+'.cs')
    (ex/p.name).write_text(relocate(contents(p)),encoding='utf8')
source = root/'Culling/Assets/Cull translated clusters.hlsl'
include = root/'Culling/Assets/Frustum triangles.hlsl'
if not include.exists(): include = assets/'Frustum triangles.hlsl'
(assets/source.name).write_text(contents(source).replace('#include "Frustum triangles.hlsl"', contents(include)), encoding='utf8')
cluster=ex/'_DrawClusterCopies.cs'
change(cluster,'    private int _sourceVersion','    private bool _reportedDepth;\n    private int _sourceVersion')
change(cluster,'        int columns =', '''        if (depth != null && !_reportedDepth)
        {
            Log.Info("Reference tier depth active: " + faces + " faces per copy", this);
            _reportedDepth = true;
        }
        int columns =''')

# Preserve the reference vertex indexing/placement and normalize both materials to the orbit's unlit RGB.
shaderDir=Path('Operators/Lib/Assets/shaders/3d/mesh')
for batch in [False,True]:
    srcShader=(root/'Culling/Assets/Draw translated clusters.hlsl') if batch else shaderDir/'mesh-DrawAtPoints.hlsl'
    shader=contents(srcShader).split('    output.pixelPosition = posInClipSpace;')[0]
    shader=shader.replace('psInput output;', 'psInput output = (psInput)0;')
    center='asfloat(cluster.x)' if batch else 'currentPoint.Position.x'
    col='colorRGB' if batch else 'color'
    rgb='lerp(float3(.04,.62,.87), float3(.94,.32,.09), ('+center+' / 2.1 + 7) / 14) * vertex.ColorRGB'
    shader+='\n    output.pixelPosition = posInClipSpace;\n    output.'+col+' = '+(rgb if batch else 'float4('+rgb+',1)')+';\n    return output;\n}\nfloat4 psMain(psInput pin) : SV_Target0 { return float4(pin.'+col+'.rgb, 1); }\n'
    filename='ReferenceRing'+('Clusters' if batch else 'Points')+'.hlsl'
    (shaderDir/filename).write_text(shader,encoding='utf8')
    if batch:
        file=ex/'DrawCulledCopies.t3'
        for asset in ['Examples:Draw translated clusters.hlsl','Lib:shaders/3d/mesh/mesh-Draw.hlsl']:change(file,asset,'Lib:shaders/3d/mesh/'+filename)
    else:
        n='ReferencePointsUnlit'; old='DrawMeshAtPoints'
        for ext in ['.t3','.t3ui','.cs']:
            v=contents(lib/(old+ext)).replace('42cb88bc-beb8-4d89-ac99-44b77be5f03e',key(n)).replace(old,n).replace('namespace Lib.mesh.draw;', 'namespace Examples.Lib.mesh;').replace('mesh-DrawAtPoints.hlsl',filename)
            (ex/(n+ext)).write_text(v,encoding='utf8')

scene=ex/'_IndirectOrbitScene.cs'
slots={'RingFine':'Geometry','RingCoarse':'Geometry','Centers':'StructuredList'}
fields='\n'.join('    [Output(Guid = "'+key(n)+'")]\n    public readonly Slot<'+typ+'> '+n+' = new();\n' for n,typ in slots.items())
change(scene,'    internal const string SampleVariable', fields+'\n    internal const string SampleVariable')
change(scene,'        Fine.UpdateAction', '''        RingFine.UpdateAction = _ => RingFine.Value ??= MakeField(24, 12, true);
        RingCoarse.UpdateAction = _ => RingCoarse.Value ??= MakeField(8, 4, true);
        Centers.UpdateAction = _ =>
        {
            if (Centers.Value != null) return;
            var points = new StructuredList<Point>(225);
            for (int i = 0; i < 225; i++)
            {
                int x = i % 15 - 7, z = i / 15 - 7;
                points.TypedElements[i] = new Point { Position = new Vector3(2.1f*x, .6f*MathF.Cos(x+z), 2.1f*z) };
            }
            Centers.Value = points;
        };
        Fine.UpdateAction''')
change(scene,'int around, int tube)', 'int around, int tube, bool template = false)')
change(scene,'const int side = 15;', 'int side = template ? 1 : 15;')
change(scene,'var start = part * perPart;', 'if (template) center = Vector3.Zero;\n            var start = part * perPart;')
change(scene,'for (var u = 0;', 'if (template) color = Vector4.One;\n            for (var u = 0;')
change(scene,'mode is 0 or 2 ? 0 : 1','mode switch { 0 or 2 => 0, 1 => 2, 5 => 3, _ => 1 }')
u=ex/'_IndirectOrbitScene.t3ui';ui=graph(u)
for n in slots:ui['OutputUis'].append(dict(OutputId=key(n),Position=dict(X=0,Y=0)))
save(u,ui)
change(ex/'_MeasureIndirectOrbit.cs','ExampleModes = [6, 7, 3, 4, 2]','ExampleModes = [7, 1, 3, 2, 4, 5]')
p=ex/'DrawGeometryIndirectExample.t3';d=graph(p)
sceneId='4ff792e8-3c8a-557b-9aa8-45078ff00296'
def node(name,symbol,sid,params=()):
    child=key(name)
    d['Children'].append(dict(Id=child,SymbolId=sid,SymbolName=symbol,InputValues=[dict(Id=i,Type=t,Value=v) for i,t,v in params],Outputs=[]))
    return child
def link(a,o,b,i):d['Connections'].append(connection(a,o,b,i))
def sym(n):return 'Examples.Lib.mesh.'+n
meshes=[]
for n in ['RingFine','RingCoarse']:
    m=node(n,'Lib.geometry.convert.GeometryToMesh','4fea8c0e-cb7f-41fe-a410-df1d3886bf98',[('94d5cf91-080f-44f4-9c8c-80a81ebd88f6','System.Boolean',False)])
    link(sceneId,key(n),m,'57469ea6-f0c5-4e8d-a53c-40e8ac495bc3');meshes.append(m)
for cull in [False,True]:
    tag=str(cull)
    split=node('split'+tag,sym('SplitPointsByLod'),'f906d318-3d5c-57c6-8357-827211240f35',[
        ('25d36485-715e-52b0-bec5-56864043390f','System.Boolean',cull),('0c95952b-f2d9-50c6-b4d9-08dabf3e1888','System.Boolean',True),
        ('0e78005d-c28e-5826-b2ec-6aff5a9dbf3f','System.Numerics.Vector2',dict(X=100,Y=100)),('7caf684a-df0d-59f8-9393-0e9e05cbe221','System.Single',.87),
        ('de63653b-911f-542d-8e7b-4a3dd8d0f62e','System.Int32',5)])
    link(sceneId,key('Centers'),split,'a5b3a04d-989a-54cb-ba4a-35aab23de6a6')
    exe=node('execute'+tag,'Lib.flow.Execute','936e4324-bea2-463a-b196-6064a2d8a6b2')
    for i,tier in enumerate(['307f7dce-9990-5a95-b31c-c4772ecf4958','4e11e15a-2880-5735-93be-de043ec71883']):
        name=tag+str(i)
        buf=node('upload'+name,sym('PointListToGpuBuffer'),graph(ex/'PointListToGpuBuffer.t3')['Id'])
        link(split,tier,buf,'88b562ef-bf4c-5005-8bff-fc2f251b70aa')
        if cull:
            draw=node('draw'+name,sym('DrawCulledCopies'),'72e51ed8-0270-51b7-bf94-bd1339f0d78c',[('5c16d6f0-d378-5ca1-a806-7bfae331de90','System.Boolean',True)])
            mi,pi,cmd='6b67caa7-6693-59f2-b050-2fc4d1d4935b','bd9e5b2f-92c0-581c-950e-243a054df4e0','38639de7-a9bc-5602-91dd-e775154db88f'
        else:
            draw=node('draw'+name,sym('ReferencePointsUnlit'),key('ReferencePointsUnlit'))
            mi,pi,cmd='329d8248-5f9f-4ad3-9b97-0f142e91ba05','408ae7c7-9aa8-4537-8c55-b5689f8f9b56','774a96e4-24e2-4e1a-a70d-63794d24dd51'
        link(meshes[i],'4bf00eb5-a39c-4567-a81e-46c2276d1418',draw,mi)
        link(buf,'494a6f9a-db09-54fb-8ee2-f9d177378b84',draw,pi)
        link(draw,cmd,exe,'5d73ebe6-9aa0-471a-ae6b-3f5bfd5a0f9c')
    src,cmd=exe,'e81c99ce-fcee-4e7c-a1c7-0aa3b352b7e1'
    if cull:
        depth=node('depthTiers',sym('DepthCullPass'),'8022279f-6259-5491-b212-9a6f84780944')
        link(src,cmd,depth,'85dc5b6a-fe05-5606-9c2f-fd1f20c90b83')
        src,cmd=depth,'6a81fee5-fce2-5b5c-a882-e4a2bfbf8ac6'
    link(src,cmd,'4507119f-61cd-5ae0-b4e8-5deef24fe666','988dd1b5-636d-4a78-9592-2c6601401cc1')
save(p,d)
timer=ex/'_MeasureIndirectOrbit.cs'
change(timer,'    private readonly Timers[]','    private readonly double[] _cpuMs = new double[Passes * Modes * Steps];\n    private readonly Timers[]')
change(timer,'        dc.End(q.Start);\n        try','        dc.End(q.Start);\n        long before = System.Diagnostics.Stopwatch.GetTimestamp();\n        try')
change(timer,'            dc.End(q.End);','            _cpuMs[sample] = System.Diagnostics.Stopwatch.GetElapsedTime(before).TotalMilliseconds;\n            dc.End(q.End);')
change(timer,'degrees,gpu_ms','degrees,gpu_ms,cpu_ms')
change(timer,'{_times[i]:F6}', '{_times[i]:F6},{_cpuMs[i]:F6}')
