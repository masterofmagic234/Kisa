"""Original flat-colour sets, projected vehicles and articulated figures.

Geometry is authored here; both players consume the same projected vector keys.
No reference-game frames or extracted models are used.
"""
from math import sin, cos, pi, sqrt, atan2

PALETTE = dict(sky='#c3c4ab', road='#494b43', sand='#b3a27e', wall='#ad9b77',
               dark='#202c2b', glass='#283c3e', white='#d8d6ba', skin='#735843')

def add(a,b): return tuple(x+y for x,y in zip(a,b))
def sub(a,b): return tuple(x-y for x,y in zip(a,b))
def mul(a,s): return tuple(x*s for x in a)
def dot(a,b): return sum(x*y for x,y in zip(a,b))
def cross(a,b): return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def unit(a): return mul(a,1/max(1e-8,sqrt(dot(a,a))))
def shade(col,f):
    v=col.lstrip('#');return '#'+''.join(f'{min(255,max(0,round(int(v[i:i+2],16)*f))):02x}' for i in (0,2,4))
def rotate(p,yaw):
    a=yaw*pi/180; x,y,z=p;return (x*cos(a)+z*sin(a),y,-x*sin(a)+z*cos(a))

class Mesh:
    def __init__(self): self.faces=[]
    def poly(self,points,color): self.faces.append((points,color));return self
    def box(self,x,y,z,w,h,d,col):
        p=[(x-w/2,y,z-d/2),(x+w/2,y,z-d/2),(x+w/2,y+h,z-d/2),(x-w/2,y+h,z-d/2),
           (x-w/2,y,z+d/2),(x+w/2,y,z+d/2),(x+w/2,y+h,z+d/2),(x-w/2,y+h,z+d/2)]
        for ids,f in [((0,1,2,3),.74),((1,5,6,2),.87),((4,7,6,5),.8),((0,3,7,4),.67),((3,2,6,7),1.08),((0,4,5,1),.56)]:
            self.poly([p[i] for i in ids],shade(col,f))
        return self
    def hull(self,z0,z1,low,high,w0,w1,col):
        p=[(-w0,low,z0),(w0,low,z0),(w0,high,z0),(-w0,high,z0),(-w1,low,z1),(w1,low,z1),(w1,high,z1),(-w1,high,z1)]
        for ids,f in [((0,1,2,3),.8),((1,5,6,2),.85),((4,7,6,5),.7),((0,3,7,4),.65),((3,2,6,7),1.05)]: self.poly([p[i] for i in ids],shade(col,f))
    def limb(self,a,b,r1,r2,col):
        axis=unit(sub(b,a)); tangent=unit(cross(axis,(0,0,1)))
        if dot(tangent,tangent)<.1:tangent=(1,0,0)
        other=unit(cross(axis,tangent))
        pts=[]
        for center,r in [(a,r1),(b,r2)]:
            pts.append([add(center,add(mul(tangent,cos(k*pi/2)*r),mul(other,sin(k*pi/2)*r))) for k in range(4)])
        for i in range(4):self.poly([pts[0][i],pts[0][(i+1)%4],pts[1][(i+1)%4],pts[1][i]],shade(col,[.8,.66,.95,1.05][i]))
        self.poly(pts[1],shade(col,1.03));return self
    def rings(self,levels,col,sides=8):
        rings=[[(cos(2*pi*i/sides+pi/8)*rx,y,sin(2*pi*i/sides+pi/8)*rz) for i in range(sides)] for y,rx,rz in levels]
        for a,b in zip(rings,rings[1:]):
            for i in range(sides):
                j=(i+1)%sides
                self.poly([a[i],a[j],b[j],b[i]],shade(col,.78+.2*max(0,sin(2*pi*(i+.5)/sides))))
        self.poly(rings[-1],shade(col,1.03));return self
    def transformed(self,pos=(0,0,0),yaw=0):
        return [( [add(rotate(p,yaw),pos) for p in ps],c) for ps,c in self.faces]

def car(kind='sedan',phase=0):
    m=Mesh(); police=kind=='police'; jeep=kind=='jeep'; limo=kind=='limo'
    body='#dddcc4' if police else '#232c2b' if limo else '#5b6453' if jeep else '#a7a394'
    length=6.1 if limo else 4.9; back=-length/2;front=length/2
    m.poly([(-1.25,.018,back-.1),(1.25,.018,back-.1),(1.25,.018,front+.25),(-1.25,.018,front+.25)],'#343e36')
    m.box(0,.48,0,2.05,.66,length,body)
    # A sloping bonnet, cabin and separate panes make the viewpoint readable.
    m.poly([(-1,.99,front),(1,.99,front),(.98,1.2,front-1.45),(-.98,1.2,front-1.45)],shade(body,1.18))
    roof_back=-1.9 if limo else -1.35;roof_front=.65
    if not jeep:m.poly([(-.94,1.13,roof_back-.4),(-.78,2.0,roof_back),(.78,2.0,roof_back),(.94,1.13,roof_back-.4)],'#344447')
    m.poly([(-.78,2.0,roof_front),(.78,2.0,roof_front),(.94,1.18,1.12),(-.94,1.18,1.12)],'#3e5353')
    if not jeep:m.poly([(-.78,2,roof_back),(-.78,2,roof_front),(.78,2,roof_front),(.78,2,roof_back)],shade(body,1.13))
    for sign in (-1,1):
        x=sign*.97;xr=sign*.79
        if not jeep:
            m.poly([(x,1.13,roof_back-.4),(xr,1.98,roof_back),(xr,1.98,roof_front),(x,1.15,1.12)],'#283d3d')
            for zz in (roof_back+.75,.1):
                m.poly([(x*1.005,1.12,zz-.055),(xr*1.005,1.99,zz-.055),(xr*1.005,1.99,zz+.035),(x*1.005,1.12,zz+.035)],shade(body,.88))
        m.box(sign*1.015,.78,.25,.032,.025,.23,'#c3c0a6')
        m.box(sign*1.12,1.12,1.05,.25,.18,.27,body)
        for z in (-length*.31,length*.31):
            for k in range(12):
                a=k*pi/6;b=(k+1)*pi/6
                x1=sign*1.01;x2=sign*1.17;r=.47;yc=.49
                m.poly([(x1,yc+r*sin(a),z+r*cos(a)),(x2,yc+r*sin(a),z+r*cos(a)),(x2,yc+r*sin(b),z+r*cos(b)),(x1,yc+r*sin(b),z+r*cos(b))],'#202522')
                m.poly([(x2,yc,z),(x2,yc+r*.62*sin(a+phase),z+r*.62*cos(a+phase)),(x2,yc+r*.62*sin(b+phase),z+r*.62*cos(b+phase))], '#8b8f80' if k%3 else '#4a5550')
    m.box(0,.54,front+.025,2.12,.16,.13,'#969c8e')
    m.box(0,.72,front+.09,.7,.19,.025,'#2a332e')
    for x in (-.7,.7):
        m.box(x,.78,front+.08,.4,.22,.03,'#d8d1a8');m.box(x,.77,back-.025,.38,.16,.035,'#893e31')
    if police:
        m.box(0,.92,0,2.075,.2,length*.95,'#3a605f')
        m.box(0,2.03,-.35,1.17,.1,.4,'#4b5149')
        for x in (-.38,.38):m.box(x,2.13,-.35,.4,.22,.36,'#b86136' if sin(phase*2+x)>0 else '#7e422e')
    if jeep:
        m.box(0,1.2,-1.6,1.8,.65,.5,body)
        m.box(0,1.55,-2.4,.8,.8,.22,'#2d3630')
        for side in (-1,1):m.box(side*.82,1.1,-1.4,.065,1,.065,'#3e493a')
        m.box(0,2.03,-1.4,1.7,.065,.065,'#3e493a')
    else:m.faces=[([(x,1.13+(y-1.13)*.64 if y>1.13 else y,z) for x,y,z in pts],col) for pts,col in m.faces]
    return m

def person(t=0,uniform='#d0cfb6',beret='#b35d32',pose='watch',head=0,skin='#71503c',elevation=0,seated=False,weapon=True):
    m=Mesh()
    if not seated:
        m.limb((-.19,.1,0),(-.17,.95,0),.15,.2,uniform);m.limb((.2,.1,.08),(.17,.95,0),.15,.2,uniform)
        m.box(-.19,.03,.1,.29,.12,.45,'#292f2b');m.box(.2,.03,.17,.29,.12,.45,'#252d29')
    m.rings([(.88,.27,.18),(1.15,.31,.19),(1.46,.38,.21),(1.57,.22,.17)],uniform)
    m.box(0,.92,0,.79,.12,.46,'#343b30')
    m.box(.09,.96,.245,.13,.1,.035,'#a79e72')
    m.box(0,1.54,0,.2,.15,.24,skin)
    face=Mesh();face.rings([(1.64,.10,.10),(1.73,.14,.145),(1.92,.17,.165),(2.025,.12,.14)],skin)
    face.poly([(-.13,1.93,.17),(.13,1.93,.17),(.10,1.77,.19),(-.10,1.77,.19)],shade(skin,1.1))
    face.poly([(-.04,1.89,.22),(.04,1.89,.22),(.035,1.82,.31),(-.03,1.81,.26)],shade(skin,1.12))
    face.rings([(2.015,.19,.18),(2.07,.205,.185),(2.12,.13,.14)],beret)
    m.faces+=face.transformed(yaw=head)
    # Joint locations change, rather than translating a cut-out of the torso.
    if pose in ('aim','raise'):
        elbow1=(-.33,1.3,.45);hand1=(-.1,1.45,.9);elbow2=(.4,1.18,.4);hand2=(.1,1.43,.62)
        if pose=='raise':
            p=min(1,max(0,t/2.2))
            blend=lambda a,b:add(a,mul(sub(b,a),p))
            elbow1=blend((-.46,1.18,0),elbow1);hand1=blend((-.3,1,.19),hand1)
            elbow2=blend((.47,1.17,.02),elbow2);hand2=blend((.34,1.02,.23),hand2)
    elif pose=='draw':
        p=min(1,max(0,t/1.6));elbow1=(-.43,1.1,.22);hand1=(-.35,.96+.34*p,.25+.25*p);elbow2=(.48,1.12,.22);hand2=(.32,.98+.37*p,.22+.4*p)
    else:
        elbow1=(-.46,1.18,.0);hand1=(-.3,1,.19);elbow2=(.47,1.17,.02);hand2=(.34,1.02,.23)
    for shoulder,elbow,hand in [((-.34,1.48,0),elbow1,hand1),((.34,1.48,0),elbow2,hand2)]:
        lift=lambda p:(p[0],p[1]+max(0,p[2]-.1)*sin(elevation*pi/180),p[2])
        elbow=lift(elbow);hand=lift(hand)
        m.limb(shoulder,elbow,.16,.115,uniform);m.limb(elbow,hand,.115,.065,uniform);m.box(hand[0],hand[1]-.07,hand[2],.11,.14,.13,skin)
    m.box(.39,.92,.13,.16,.32,.18,'#3d3d2f')
    if pose in ('aim','raise') and weapon:
        p=1 if pose=='aim' else min(1,max(0,t/2.2))
        gun=Mesh().box(.04,1.42,.85,.13,.15,.92,'#303b37').box(.04,1.45,1.53,.055,.055,.55,'#202b28').box(.04,1.22,.75,.09,.25,.15,'#212c27')
        for pts,col in gun.faces:
            angle=(1-p)*.8
            m.poly([(x,1.1+(y-1.1)*cos(angle)-(z-.25)*sin(angle)+max(0,z-.1)*sin(elevation*pi/180),.25+(y-1.1)*sin(angle)+(z-.25)*cos(angle)) for x,y,z in pts],col)
    return m

class Camera:
    def __init__(self,pos,target,focal=1000):
        self.pos=pos;self.fwd=unit(sub(target,pos));self.right=unit(cross((0,1,0),self.fwd));self.up=cross(self.fwd,self.right);self.focal=focal
    def project(self,p):
        v=sub(p,self.pos);z=max(.1,dot(v,self.fwd));return [round(640+self.focal*dot(v,self.right)/z,1),round(370-self.focal*dot(v,self.up)/z,1)]
    def depth(self,p):return dot(sub(p,self.pos),self.fwd)
    def clip(self,points,near=.7):
        out=[]
        for a,b in zip(points,points[1:]+points[:1]):
            da=self.depth(a);db=self.depth(b)
            if da>=near:out.append(a)
            if (da>=near)!=(db>=near):out.append(add(a,mul(sub(b,a),(near-da)/(db-da))))
        return out

def project_scene(frame_fn,camera_fn,duration=8,samples=17,background='#b9bca4',loop=True):
    """Stable face order and linear vertex keys, with culling for off-screen faces."""
    times=[duration*i/(samples-1) for i in range(samples)]
    frames=[frame_fn(t) for t in times];cams=[camera_fn(t) for t in times]
    assert len({len(f) for f in frames})==1,'Geometry topology must remain stable.'
    mid=len(times)//2
    def painter_order(i):
        pts=frames[mid][i][0]
        # Ground, road and contact shadows have an explicit painter layer.
        if all(p[1]<=.04 for p in pts):
            span=max(p[0] for p in pts)-min(p[0] for p in pts)
            depth=max(p[2] for p in pts)-min(p[2] for p in pts)
            return -1e7 if span>100 else -1e7+1 if depth>100 else -1e7+2
        return -sum(cams[mid].depth(p) for p in pts)/len(pts)
    order=sorted(range(len(frames[mid])),key=painter_order)
    nodes=[]
    for idx in order:
        snapshots=[];alphas=[]
        for frame,cam in zip(frames,cams):
            pts,c=frame[idx];clipped=cam.clip(pts);projected=[cam.project(p) for p in clipped]
            snapshots.append(projected)
            alphas.append(1 if len(projected)>=3 else 0)
        if not any(alphas):continue
        count=max(len(p) for p in snapshots)
        fallback=next(p for p in snapshots if len(p)>=3)
        snapshots=[(p if len(p)>=3 else fallback) for p in snapshots]
        snapshots=[p+[p[-1]]*(count-len(p)) for p in snapshots]
        base=snapshots[0]
        if all(all(x<0 for x,y in p) or all(x>1280 for x,y in p) or all(y<0 for x,y in p) or all(y>720 for x,y in p) for p in snapshots):continue
        node={'type':'polygon','points':base,'fill':frames[mid][idx][1]}
        changing=any(p!=base for p in snapshots[1:]) or min(alphas)<1
        if changing:
            node['keys']=[{'time':round(t,3),'points':p,**({'alpha':a} if min(alphas)<1 else {})} for t,p,a in zip(times,snapshots,alphas)]
            # Perspective motion is often nearly linear over several samples.
            # Keep keys only where omission moves a vertex by more than .4px.
            keys=node['keys']
            def retain(lo,hi):
                if hi<=lo+1:return [lo,hi]
                worst=-1;split=lo+1
                for i in range(lo+1,hi):
                    q=(keys[i]['time']-keys[lo]['time'])/(keys[hi]['time']-keys[lo]['time'])
                    error=max(abs(v-(a+(b-a)*q)) for point,pa,pb in zip(keys[i]['points'],keys[lo]['points'],keys[hi]['points']) for v,a,b in zip(point,pa,pb))
                    error=max(error,100*abs(keys[i].get('alpha',1)-(keys[lo].get('alpha',1)+(keys[hi].get('alpha',1)-keys[lo].get('alpha',1))*q)))
                    if error>worst:worst=error;split=i
                if worst<=.4:return [lo,hi]
                return retain(lo,split)[:-1]+retain(split,hi)
            node['keys']=[keys[i] for i in retain(0,len(keys)-1)]
            if loop:node['loop']=duration
        nodes.append(node)
    return {'background':background,'duration':duration,'nodes':nodes}

def street_set(style='town',travel=0):
    m=Mesh();m.poly([(-200,0,-200),(-200,0,500),(200,0,500),(200,0,-200)],'#a99d78')
    m.poly([(-6,.012,-150),(-6,.012,500),(6,.012,500),(6,.012,-150)],'#4e5149')
    for sign in (-1,1):
        m.box(sign*6.4,.02,60,.75,.16,250,'#c6b99a')
        for i in range(12):
            z=-24+i*16-travel;w=7;h=3.8+(i%2)*1.3;d=14.5
            col=['#a58c65','#b4a080'][i%2]
            m.box(sign*(10.5+w/2),0,z,w,h,d,col)
            m.box(sign*(10.5+w/2),h,z,w+.35,.2,d+.3,shade(col,.75))
            x=sign*10.49
            for j in range(3):
                zz=z-4.3+j*4
                m.box(x,1.2,zz,.05,1.6,1.2,'#47534c')
                m.box(x+sign*.1,1.1,zz,.25,.12,1.5,'#c1b28d')
    return m.faces

def road_marks(t):
    m=Mesh()
    for i in range(32):
        z=-40+i*8-t*4
        m.poly([(-.055,.035,z),(.055,.035,z),(.055,.035,z+3.4),(-.055,.035,z+3.4)],'#b8b9a1')
    return m.faces

def convoy_scene(variant):
    if variant=='overhead':
        cam=lambda t:Camera((14,25,-13),(0,0,18),1150)
        positions=[(-2.5,4,'police'),(0,4,'police'),(2.5,4,'police'),(-1.4,17,'sedan'),(1.4,17,'sedan'),(0,33,'limo')]
    elif variant=='flanks':
        cam=lambda t:Camera((12,9,-14),(0,1,12),1100)
        positions=[(-3.4,5,'jeep'),(3.4,5,'jeep'),(-3.4,17,'jeep'),(3.4,17,'jeep'),(0,3,'limo'),(0,15,'limo'),(0,27,'limo')]
    elif variant=='civilian':
        cam=lambda t:Camera((7,3.4,-8),(0,1,7),1080);positions=[(-1.4,6,'sedan'),(1.4,6,'sedan')]
    elif variant=='rear':
        cam=lambda t:Camera((7,4.8,-10),(0,1,10),1080);positions=[(-1.6,6,'sedan'),(1.6,6,'sedan'),(-1.6,18,'sedan'),(1.6,18,'sedan')]
    elif variant=='limo':
        cam=lambda t:Camera((7,3,-7),(0,1,4),1050);positions=[(0,4,'limo'),(-1,19,'limo'),(1,33,'limo')]
    else:
        cam=lambda t:Camera((7,4.6,-9),(0,1.05,6),1050)
        positions=[(-2.6,5,'police'),(0,5,'police'),(2.6,5,'police'),(0,23,'sedan')]
    def frame(t):
        mesh=street_set(travel=t*4)+road_marks(t)
        # Lamp standards pass the tracking camera; their perspective changes naturally.
        for side in (-1,1):
            for z0 in (3,35,67):
                z=z0-t*4
                pole=Mesh().box(side*7,0,z,.14,6,.14,'#586253').box(side*6.4,5.95,z,1.3,.1,.14,'#586253')
                mesh+=pole.faces
        for x,z,kind in positions:
            mesh+=car(kind,t*8).transformed((x,.012*sin(t*8),z),.35*sin(t*pi/4))
            if variant=='rear' and x>0 and z==6:
                rifle=Mesh().box(0,0,0,.07,.08,1.2,'#26352e')
                mesh+=rifle.transformed((x+1.1,1.4,z+.3),75)
        return mesh
    return project_scene(frame,cam,8,25)

def wheel_scene():
    # Close camera travels with the car; axle and rotating hubs stay connected.
    def frame(t):
        m=Mesh();m.poly([(-100,0,-100),(100,0,-100),(100,0,100),(-100,0,100)],'#51544b')
        for i in range(8):
            z=-18+i*6-(t*10)%6
            m.box(-2,.025,z,.12,.01,2,'#bfbea3')
        return m.faces+car('limo',t*8).faces
    return project_scene(frame,lambda t:Camera((3.4,1.13,2.8),(.9,.65,1.3),1050),4,33,'#777c68')

def cordon_scene():
    background=street_set()
    def frame(t):
        out=list(background)
        for i,z in enumerate((1,8,15,22,29,36)):
            out+=person(t,beret='#b56535' if i%2 else '#2b3430',head=18*sin(t*pi/5+i)).transformed((6.7,0,z),90)
        return out
    return project_scene(frame,lambda t:Camera((2,2.3,-2),(7,1.1,12),1060),10,21)

def hand_scene():
    # An uncluttered belt detail with an articulated hand approaching a holster.
    return {'background':'#777d68','duration':9,'nodes':[
        {'type':'polygon','points':[[0,0],[1280,0],[1280,720],[0,720]],'fill':'#b5b49b'},
        {'type':'polygon','points':[[180,0],[1000,0],[1110,720],[120,720]],'fill':'#d0ceb4'},
        {'type':'polygon','points':[[870,0],[1000,0],[1110,720],[840,720]],'fill':'#acaf98'},
        {'type':'polygon','points':[[140,300],[1043,304],[1058,388],[126,384]],'fill':'#333d32'},
        {'type':'polygon','points':[[467,313],[568,314],[568,372],[465,372]],'fill':'#a69b68'},
        {'type':'polygon','points':[[479,324],[554,325],[553,360],[477,360]],'fill':'#3f4837'},
        {'type':'polygon','points':[[720,332],[868,348],[880,586],[804,644],[730,590]],'fill':'#474734'},
        {'type':'polygon','points':[[740,315],[845,321],[841,389],[741,379]],'fill':'#26332e'},
        {'type':'group','transform':{'x':945,'y':89,'rotation':-12},'keys':[{'time':0,'rotation':-12},{'time':2.2,'rotation':4},{'time':5.5,'rotation':4},{'time':8,'rotation':0}], 'children':[
            {'type':'polygon','points':[[-90,-160],[70,-160],[62,151],[-67,170]],'fill':'#dcdbc1'},
            {'type':'polygon','points':[[32,-160],[70,-160],[62,151],[4,160]],'fill':'#aaa88f'},
            {'type':'polygon','points':[[-67,150],[62,140],[72,197],[-53,214]],'fill':'#b6b69d'},
            {'type':'polygon','points':[[-49,212],[67,193],[86,279],[70,326],[15,350],[-20,320],[-31,278],[-64,270],[-81,246]],'fill':'#75543c'},
            {'type':'polygon','points':[[35,212],[67,193],[86,279],[70,326],[45,334],[54,272]],'fill':'#60432f'},
            {'type':'line','points':[[-4,267],[16,309],[26,331]],'stroke':'#583f2e','line_width':3}
        ]}]}

def motorcycle(t):
    m=Mesh()
    for z in (-.85,.85):
        for k in range(12):
            a=k*pi/6+t*10;b=(k+1)*pi/6+t*10
            m.poly([(.12,.48,z),(.12,.48+.43*sin(a),z+.43*cos(a)),(.12,.48+.43*sin(b),z+.43*cos(b))], '#2b3330' if k%2 else '#535b51')
            m.poly([(-.12,.48+.43*sin(a),z+.43*cos(a)),(.12,.48+.43*sin(a),z+.43*cos(a)),(.12,.48+.43*sin(b),z+.43*cos(b)),(-.12,.48+.43*sin(b),z+.43*cos(b))],'#252e29')
    m.box(0,.75,0,.6,.35,1.3,'#bdc1ae');m.box(0,1.18,.7,.7,.28,.18,'#bdc1ae')
    m.box(0,1.3,.83,.45,.4,.04,'#546b69');m.box(0,1.01,-.2,.55,.12,.7,'#29352e')
    m.limb((-.22,1.18,-.2),(-.33,.47,.12),.16,.11,'#c3c6b2');m.limb((.22,1.18,-.2),(.33,.47,.12),.16,.11,'#c3c6b2')
    m.hull(-.43,-.1,1.15,1.8,.36,.3,'#d4d5bd')
    for side in (-1,1):m.limb((side*.3,1.72,-.2),(side*.42,1.34,.5),.14,.08,'#c7cbb9')
    m.box(0,1.82,-.16,.38,.4,.36,'#d9d9c0');m.box(0,1.95,.045,.34,.13,.04,'#303f3d')
    return m

def motorcycle_scene():
    def frame(t):
        out=street_set(travel=t*4)+road_marks(t)
        for x,z in [(0,2),(-1.3,5),(1.3,5),(-2.6,8),(2.6,8),(-3.9,11),(3.9,11)]:out+=motorcycle(t).transformed((x,0,z))
        return out
    return project_scene(frame,lambda t:Camera((9,7,-11),(0,1,8),1050),8,25)

def warehouse_set():
    m=Mesh();m.poly([(-100,0,-100),(100,0,-100),(100,0,180),(-100,0,180)],'#a39c7a')
    m.poly([(-5,.01,-100),(5,.01,-100),(5,.01,180),(-5,.01,180)],'#55584d')
    for side in (-1,1):
        for i in range(4):
            z=2+i*22;x=side*17;m.box(x,0,z,15,4.1,18,'#8e927d')
            m.poly([(x-7.8,4.1,z-9.3),(x,5.9,z-9.3),(x,5.9,z+9.3),(x-7.8,4.1,z+9.3)],'#777e6b')
            m.poly([(x,5.9,z-9.3),(x+7.8,4.1,z-9.3),(x+7.8,4.1,z+9.3),(x,5.9,z+9.3)],'#adb29a')
            for j in range(18):m.box(x-side*7.51,.12,z-8.5+j,.035,3.88,.09,'#6e7b6a')
            m.box(x-side*7.56,.04,z,0.04,3.05,3.3,'#47584c')
        for i in range(18):
            z=-9+i*5;m.box(side*7.6,0,z,.13,2.6,.13,'#4c5d4f')
            for y in (1.9,2.45):
                m.poly([(side*7.6,y,z),(side*7.6,y+.02,z),(side*7.6,y+.02,z+5),(side*7.6,y,z+5)],'#3f5147')
            m.limb((side*7.6,2.5,z),(side*7.6,2.75,z+.35),.015,.015,'#3b5145')
    return m.faces

def warehouse_scene(variant):
    if variant=='wire':
        nodes=[{'type':'polygon','points':[[0,0],[1280,0],[1280,720],[0,720]],'fill':'#a9afa0'},
               {'type':'polygon','points':[[0,180],[1280,95],[1280,720],[0,720]],'fill':'#788677'},
               {'type':'polygon','points':[[0,180],[850,122],[1020,240],[0,310]],'fill':'#9fa995'}]
        for x in range(-40,1340,38):
            nodes.append({'type':'line','points':[[x,275],[x+15,720]],'stroke':'#637666','line_width':3})
        for x in (162,1040):
            nodes.append({'type':'polygon','points':[[x-15,100],[x+13,100],[x+42,720],[x+3,720]],'fill':'#3e5549'})
        for base in (300,465):
            nodes.append({'type':'line','points':[[0,base+70],[1280,base-70]],'stroke':'#293f37','line_width':4})
            nodes.append({'type':'line','points':[[0,base+77],[1280,base-63]],'stroke':'#617264','line_width':2})
            for x in range(24,1280,86):
                y=base+70-x*140/1280
                nodes.append({'type':'line','points':[[x-13,y-22],[x+11,y+21]],'stroke':'#293f37','line_width':4})
                nodes.append({'type':'line','points':[[x-17,y+14],[x+16,y-16]],'stroke':'#293f37','line_width':3})
        return {'background':'#a9afa0','duration':20,'nodes':nodes}
    bg=warehouse_set()
    if variant=='wire':camera=lambda t:Camera((5.2,2.3,-4+t*.35),(8,2.1,3+t*.35),1050)
    elif variant=='roof':camera=lambda t:Camera((2,2.3,-12+t*.35),(17,5.4,9),1100)
    else:camera=lambda t:Camera((2,3,-11+t*.35),(0,1,24+t*.35),1050)
    return project_scene(lambda t:bg,camera,30,31,'#b9bca4',False)

def attack_scene(variant):
    bg=warehouse_set()
    if variant=='rocket':
        nodes=[{'type':'polygon','points':[[0,0],[1280,0],[1280,720],[0,720]],'fill':'#a9b8ae'}]
        for i in range(13):
            x=i*110-50;y=510+(i%3)*14
            nodes.append({'type':'polygon','points':[[x,720],[x,y],[x+42,y-38],[x+99,y],[x+99,720]],'fill':['#637a6c','#708578','#7a8e80'][i%3]})
        nodes.extend([
            {'type':'polygon','points':[[0,570],[1280,545],[1280,720],[0,720]],'fill':'#3e5b4d'},
            {'type':'polygon','points':[[0,650],[180,524],[360,635],[1280,600],[1280,720],[0,720]],'fill':'#596e5c'},
            {'type':'line','points':[[210,620],[210,620]],'stroke':'#cbd0b7','line_width':5,'keys':[{'time':0,'points':[[210,620],[210,620]],'alpha':0},{'time':.5,'points':[[210,620],[210,620]],'alpha':1},{'time':3.5,'points':[[210,620],[982,204]],'alpha':.8},{'time':6,'points':[[210,620],[982,204]],'alpha':0}]},
            {'type':'group','transform':{'x':210,'y':620,'rotation':62,'alpha':0},'keys':[{'time':0,'x':210,'y':620,'alpha':0},{'time':.5,'x':210,'y':620,'alpha':1},{'time':3.5,'x':982,'y':204,'alpha':1},{'time':3.58,'x':995,'y':196,'alpha':0}], 'children':[
                {'type':'polygon','points':[[-4,18],[-4,-19],[0,-28],[5,-19],[5,18]],'fill':'#ddd7ad'},
                {'type':'polygon','points':[[-4,14],[0,32],[5,14]],'fill':'#c7a478'}]},
            {'type':'group','transform':{'x':992,'y':200,'scale_x':.05,'scale_y':.05,'alpha':0},'keys':[{'time':0,'alpha':0},{'time':3.5,'alpha':0,'scale_x':.05,'scale_y':.05},{'time':3.65,'alpha':.9,'scale_x':.7,'scale_y':.7},{'time':4.3,'alpha':.7,'scale_x':1,'scale_y':1},{'time':12,'alpha':.3,'scale_x':1.3,'scale_y':1.3}], 'children':[
                {'type':'polygon','points':[[-76,4],[-63,-29],[-32,-42],[-17,-62],[18,-60],[44,-32],[70,-15],[79,13],[58,36],[20,48],[-20,40],[-49,26]],'fill':'#c1c5af'},
                {'type':'polygon','points':[[-27,-11],[-17,-29],[15,-27],[37,-7],[28,15],[4,23],[-22,13]],'fill':'#d7cd9e'}]}
        ])
        return {'background':'#a9b8ae','duration':12,'nodes':nodes}
    if variant in ('gunman','rocket'):
        cam=lambda t:Camera((8,5.3,-9),(17,5.7,1),1100)
        def frame(t):
            out=list(bg);p=min(1,max(0,(t-7.5)/1.8))
            fallen=max(0,min(1,(t-17.4)/.7))
            fig=person(t,uniform='#596858',beret='#394b42',pose='aim',skin='#655445',weapon=False)
            # Leaning/rising changes the arm/body perspective before the shot.
            out+=fig.transformed((16,3.7+p*1.6-fallen*2.1,0),-65-fallen*50)
            launcher=Mesh().box(0,0,0,.18,.2,1.8,'#334940').box(0,.02,-.6,.36,.28,.35,'#4c5b49')
            out+=launcher.transformed((15.8,5.2+p*1.6-fallen*2.0,0),-65-fallen*55)
            if variant=='rocket':
                q=max(0,min(1,(t-.5)/3));r=Mesh().box(0,0,0,.09,.09,.65,'#dfd7a5')
                out+=r.transformed((15.8-q*4,6.7+q*9,q*13),-30)
            return out
        return project_scene(frame,cam,22 if variant=='gunman' else 12,45 if variant=='gunman' else 25,'#aeb5a8',False)
    if variant=='response':
        cam=lambda t:Camera((4,2,-5),(0,1.6,5),1100)
        def frame(t):
            out=list(bg)
            out+=car('jeep',0).transformed((0,0,6))
            for x,z in [(-.55,5.6),(.55,5.6),(.1,6.6)]:
                yaw=atan2(16-x,-z)*180/pi
                out+=person(t,uniform='#596b59',beret='#8e3730',pose='raise',head=-12,skin='#b4977a',elevation=18,seated=True).transformed((x,.2,z),yaw)
            return out
        scene=project_scene(frame,cam,9,19,'#adb7a8',False)
        for x,z in [(-.55,5.6),(.55,5.6),(.1,6.6)]:
            yaw=atan2(16-x,-z)*180/pi
            center=cam(4).project(add(rotate((.04,1.45+1.7*sin(18*pi/180),1.8),yaw),(x,.2,z)))
            xx,yy=center
            scene['nodes'].append({'type':'polygon','points':[[xx-4,yy-3],[xx-19,yy-16],[xx-8,yy+3],[xx-20,yy+15],[xx+5,yy+6],[xx+15,yy]],'fill':'#d8b47a','transform':{'alpha':0},'keys':[{'time':0,'alpha':0},{'time':4,'alpha':0},{'time':4.06,'alpha':1},{'time':4.16,'alpha':0},{'time':4.36,'alpha':0},{'time':4.4,'alpha':1},{'time':4.5,'alpha':0},{'time':4.7,'alpha':0},{'time':4.75,'alpha':1},{'time':4.85,'alpha':0}]})
        return scene
    cam=lambda t:Camera((2,2.3,-3),(6.6,1.5,8),1100)
    def frame(t):
        out=list(bg)
        for i,z in enumerate((6,10,15)):
            out+=person(t,pose='draw' if t<2 else 'aim',beret='#aa693a' if i%2 else '#2d3e34').transformed((6.5,0,z),-80)
        return out
    # Stable topology across the draw/aim action, by keeping the rifle visible at all times.
    def stable(t):
        out=list(bg)
        for i,z in enumerate((6,10,15)):
            out+=person(max(0,t-1.5),pose='raise',beret='#aa693a' if i%2 else '#2d3e34',head=12*sin(t)).transformed((6.5,0,z),-80)
        return out
    return project_scene(stable,cam,9,19,'#adb7a8',False)

def build_scenes():
    scenes={
        'convoy_tracking':convoy_scene('tracking'), 'convoy_overhead':convoy_scene('overhead'),
        'convoy_wheel':wheel_scene(), 'president_limo':convoy_scene('limo'),
        'convoy_flanks':convoy_scene('flanks'),'convoy_civilian':convoy_scene('civilian'),
        'convoy_rear':convoy_scene('rear'),
        'street_cordon':cordon_scene(),'street_hand':hand_scene(),
        'escort_motorcycles':motorcycle_scene(),
    }
    for v in ('wide','wire','roof'):scenes['warehouses_'+v]=warehouse_scene(v)
    for v in ('gunman','response','rocket','late_police'):scenes['attack_memory_'+v]=attack_scene(v)
    return scenes
