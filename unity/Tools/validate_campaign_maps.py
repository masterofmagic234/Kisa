#!/usr/bin/env python3
"""Check authored campaign navigation and 18-pixel vehicle routes without Unity.

Walls and solid props are axis-aligned rectangles. Doors are intentionally omitted
from radius8/cell12 navigation, matching the existing source sampler's door policy.
Vehicles are checked analytically as an 18px swept circle against those rectangles.
This does not execute Unity collider recovery, dynamic actors, triggers or NPC AI.
"""
from __future__ import annotations
import argparse
from collections import deque
import hashlib
import json
import math
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
DEFAULT=ROOT/'unity/WhiteGuard/Assets/Resources/WhiteGuardCampaign/campaign.json'

def point_rect_distance2(x,y,r):
    return (x-min(max(x,r['x']),r['x']+r['w']))**2+(y-min(max(y,r['y']),r['y']+r['h']))**2

def inside_circle(x,y,radius,wall):
    return point_rect_distance2(x,y,wall)<radius*radius-1e-8

def cross(a,b,c):return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
def point_segment_distance2(p,a,b):
    dx,dy=b[0]-a[0],b[1]-a[1];length2=dx*dx+dy*dy
    t=max(0,min(1,((p[0]-a[0])*dx+(p[1]-a[1])*dy)/length2)) if length2 else 0
    return (p[0]-a[0]-t*dx)**2+(p[1]-a[1]-t*dy)**2

def on_segment(p,a,b):
    return abs(cross(a,b,p))<1e-8 and min(a[0],b[0])-1e-8<=p[0]<=max(a[0],b[0])+1e-8 and min(a[1],b[1])-1e-8<=p[1]<=max(a[1],b[1])+1e-8

def segments_distance2(a,b,c,d):
    ab_c,ab_d,cd_a,cd_b=cross(a,b,c),cross(a,b,d),cross(c,d,a),cross(c,d,b)
    if ab_c*ab_d<0 and cd_a*cd_b<0:return 0
    if on_segment(a,c,d) or on_segment(b,c,d) or on_segment(c,a,b) or on_segment(d,a,b):return 0
    return min(point_segment_distance2(a,c,d),point_segment_distance2(b,c,d),point_segment_distance2(c,a,b),point_segment_distance2(d,a,b))

def swept_rect_distance2(a,b,r):
    if point_rect_distance2(*a,r)==0 or point_rect_distance2(*b,r)==0:return 0
    x,y,w,h=r['x'],r['y'],r['w'],r['h']
    corners=[(x,y),(x+w,y),(x+w,y+h),(x,y+h)]
    return min(segments_distance2(a,b,corners[i],corners[(i+1)%4]) for i in range(4))

def validate(campaign):
    failures=[];reports=[];checks=0
    def require(value,message):
        nonlocal checks
        checks+=1
        if not value:failures.append(message)
    for level in campaign['levels']:
        name=level['id'];w,h=level['width'],level['height']
        walls=level['walls']+[r for r in level.get('props',[]) if r.get('solid',False)]
        require(w>32 and h>32,name+': positive map bounds')
        for wall in walls:
            require(wall['w']>0 and wall['h']>0,name+': valid rectangle '+wall['id'])
        objective_ids={obj['id'] for obj in level.get('objectives',[])}
        for enemy in level.get('enemies',[]):
            activation=enemy.get('activeAfterObjective','')
            if activation:require(activation in objective_ids,name+': enemy '+enemy['id']+' has a valid activation objective')
        entities=[]
        entities.append(('spawn',{'id':name+'_spawn',**level['spawn']}))
        for group in ['enemies','actors','pickups','interactions']:
            entities.extend((group,item) for item in level.get(group,[]) if item.get('role')!='passenger')
        for kind,item in entities:
            x,y=item['x'],item['y']
            require(7<x<w-7 and 7<y<h-7,name+': '+item['id']+' inside bounds')
            require(not any(inside_circle(x,y,7,wall) for wall in walls),name+': '+item['id']+' has radius7 body clearance')
        grid={}
        for gy in range(1,int(h/12)-1):
            for gx in range(1,int(w/12)-1):
                x,y=gx*12+6,gy*12+6
                if not any(inside_circle(x,y,8,wall) for wall in walls):grid[(gx,gy)]=(x,y)
        def nearest(point):
            cell=(int(point[0]//12),int(point[1]//12))
            if cell in grid:return cell
            candidates=[key for key in grid if abs(key[0]-cell[0])<=6 and abs(key[1]-cell[1])<=6]
            return min(candidates,key=lambda key:math.dist(grid[key],point)) if candidates else None
        start=nearest((level['spawn']['x'],level['spawn']['y']))
        require(start is not None,name+': spawn connects to navigation')
        seen={start} if start is not None else set();pending=deque(seen)
        while pending:
            c=pending.popleft()
            for nxt in [(c[0]+1,c[1]),(c[0]-1,c[1]),(c[0],c[1]+1),(c[0],c[1]-1)]:
                if nxt in grid and nxt not in seen:seen.add(nxt);pending.append(nxt)
        for kind,item in entities:
            require(nearest((item['x'],item['y'])) in seen,name+': navigation reaches '+item['id'])
        for region in level.get('regions',[])+[level['exit']]:
            require(any(cell in seen and region['x']<=point[0]<=region['x']+region['w'] and region['y']<=point[1]<=region['y']+region['h'] for cell,point in grid.items()),name+': navigation reaches region '+region['id'])
        vehicles=0;route_legs=0
        actor_ids={a['id']:a for a in level.get('actors',[])}
        for actor in level.get('actors',[]):
            if actor.get('role')=='passenger':
                host=actor_ids.get(actor.get('transport',''))
                require(host is not None and host.get('role')=='vehicle',name+': passenger '+actor['id']+' has a vehicle')
            if actor.get('role')!='vehicle':continue
            vehicles+=1;previous=(actor['x'],actor['y'])
            require(not any(inside_circle(*previous,18,wall) for wall in walls),name+': vehicle '+actor['id']+' spawn radius18 clear')
            for index,waypoint in enumerate(actor.get('route',[])):
                current=(waypoint['x'],waypoint['y']);route_legs+=1
                require(0<math.dist(previous,current)<=100,name+': vehicle '+actor['id']+' waypoint '+str(index)+' spacing in(0,100]')
                require(not any(swept_rect_distance2(previous,current,wall)<18*18-1e-8 for wall in walls),name+': vehicle '+actor['id']+' leg '+str(index)+' swept radius18 clear')
                previous=current
        reports.append(dict(id=name,walkable_cells=len(grid),reachable_cells=len(seen),entities=len(entities),vehicles=vehicles,route_legs=route_legs))
    return dict(status='PASS' if not failures else 'FAIL',checks=checks,failures=failures,levels=reports,
        navigation_cell_pixels=12,navigation_radius_pixels=8,vehicle_swept_radius_pixels=18,
        unity_editor_executed=False,scope='Static authored rectangle geometry/navigation only; no native collision recovery, dynamic actors, trigger or AI execution.')

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('campaign',nargs='?',type=Path,default=DEFAULT)
    parser.add_argument('--report',type=Path,default=ROOT/'unity/Validation/Campaign/map-geometry.json')
    args=parser.parse_args();raw=args.campaign.read_bytes();report=validate(json.loads(raw))
    report['campaign_sha256']=hashlib.sha256(raw).hexdigest()
    args.report.parent.mkdir(parents=True,exist_ok=True);args.report.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
    print(json.dumps(report,ensure_ascii=False,indent=2));raise SystemExit(bool(report['failures']))

if __name__=='__main__':main()
