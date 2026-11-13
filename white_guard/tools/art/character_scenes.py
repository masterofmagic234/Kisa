"""Original flat-colour character staging for the White Guard opening.

All drawing and motion are authored as editable polygon groups. There are no
photographs, traced frames or sprites translated across a background here.
Coordinates are in a 1280 x 720 design frame; angles are degrees.
"""
from copy import deepcopy

INK = '#172426'
DARK = '#202c2b'
MID = '#344644'
OLIVE = '#58604b'
LIGHT = '#829080'
SAND = '#c0b38e'
PAPER = '#d2c6a3'
CREAM = '#d4c9ab'
RUST = '#9a6146'
SKIN = '#b58e71'
SKIN_D = '#896c58'
SKIN_L = '#d1af8b'
BLACK_SKIN = '#735343'
BLACK_D = '#513d34'
BLACK_L = '#99775a'


def poly(points, fill, **kw):
    return dict(type='polygon', points=[list(p) for p in points], fill=fill, **kw)


def box(x, y, w, h, fill, **kw):
    return poly([(x,y),(x+w,y),(x+w,y+h),(x,y+h)],fill,**kw)


def ellipse(x,y,w,h,fill,**kw):
    return dict(type='ellipse',rect=[x,y,w,h],fill=fill,**kw)


def line(points, color, width=2, **kw):
    return dict(type='line',points=[list(p) for p in points],stroke=color,line_width=width,**kw)


def group(children, x=0,y=0,scale=1,rotation=0,keys=None,**kw):
    node=dict(type='group',children=children,transform=dict(x=x,y=y,scale_x=scale,scale_y=scale,rotation=rotation),**kw)
    if keys: node['keys']=keys
    return node


def scene(nodes, background='#a29374',duration=18):
    return dict(background=background,duration=duration,nodes=nodes)


def flourish_signature(x,y,w,h,color='#58634e',width=3):
    """An extravagant authored pen flourish, fitted to the document surface."""
    contour=[(0,.61),(.08,.30),(.16,.05),(.21,.09),(.20,.31),(.12,.77),(.07,.90),(.04,.80),
             (.10,.54),(.22,.44),(.29,.47),(.24,.65),(.19,.65),(.20,.51),(.30,.41),(.37,.48),
             (.32,.66),(.28,.67),(.32,.49),(.40,.29),(.45,.05),(.49,.03),(.50,.20),(.46,.42),
             (.40,.71),(.38,.86),(.42,.91),(.50,.81),(.57,.57),(.64,.48),(.70,.54),(.66,.69),
             (.61,.66),(.65,.51),(.74,.49),(.81,.58),(.77,.73),(.72,.70),(.77,.54),(.86,.54),
             (.93,.64),(.96,.58),(.94,.46),(.88,.52),(.85,.72),(.89,.85),(1,.84),(.94,.95),
             (.75,.99),(.49,.92),(.20,.94),(.10,.88),(.24,.81),(.47,.79),(.75,.82),(.99,.88)]
    return line([(x+a*w,y+b*h) for a,b in contour],color,width)


def round_seal(x,y,size,color):
    return [ellipse(x,y,size,size,color),ellipse(x+size*.065,y+size*.065,size*.87,size*.87,PAPER),
            ellipse(x+size*.13,y+size*.13,size*.74,size*.74,color),ellipse(x+size*.17,y+size*.17,size*.66,size*.66,PAPER),
            line([(x+size*.27,y+size*.49),(x+size*.43,y+size*.34),(x+size*.65,y+size*.59),(x+size*.74,y+size*.43)],color,max(.8,size*.026))]


def head(skin=SKIN, hair='#343c36', profile=False, turn=True, cap=False, helmet=False):
    """Head pivot at base of neck, with separately posed head contour and hair."""
    shadow=BLACK_D if skin==BLACK_SKIN else SKIN_D
    light=BLACK_L if skin==BLACK_SKIN else SKIN_L
    front=[(-38,-94),(-34,-137),(-17,-158),(19,-163),(43,-145),(48,-120),(48,-109),(45,-91),(39,-64),(17,-43),(-18,-45),(-35,-65)]
    side=[(-35,-94),(-30,-138),(-13,-159),(21,-159),(40,-141),(42,-118),(57,-97),(43,-91),(39,-62),(17,-43),(-16,-47),(-33,-66)]
    contour=side if profile else front
    contour_keys=None
    if turn:
        target=front if profile else side
        contour_keys=[dict(time=0,points=[list(p) for p in contour]),dict(time=2.5,points=[list(p) for p in contour]),dict(time=3.6,points=[list(p) for p in target]),dict(time=6.5,points=[list(p) for p in target]),dict(time=7.8,points=[list(p) for p in contour]),dict(time=18,points=[list(p) for p in contour])]
    contour_node=poly(contour,skin)
    if contour_keys: contour_node['keys']=contour_keys
    nodes=[poly([(-19,-55),(22,-55),(24,-20),(-27,-20)],shadow),
           poly([(-18,-55),(8,-48),(13,-20),(-26,-20)],skin),
           contour_node,
           poly([(-33,-130),(-16,-143),(-9,-112),(-14,-84),(-7,-54),(-23,-49),(-36,-70)],shadow),
           poly([(14,-141),(33,-135),(43,-125),(41,-91),(29,-62),(13,-54),(20,-80),(29,-108)],light),
           poly([(-37,-127),(-32,-148),(-14,-166),(21,-170),(45,-149),(46,-126),(30,-139),(7,-143),(-10,-138),(-16,-115),(-30,-113)],hair),
           poly([(-25,-152),(-8,-163),(18,-163),(28,-154),(6,-156)],'#566056'),
           poly([(-31,-111),(-18,-113),(-17,-89),(-24,-83),(-33,-91)],skin),
           poly([(-29,-103),(-22,-106),(-23,-93),(-28,-94)],shadow)]
    profile_face=[poly([(28,-118),(43,-114),(36,-110),(28,-110)],INK),poly([(37,-108),(46,-98),(38,-96)],shadow),line([(30,-76),(40,-77)],shadow,2)]
    front_face=[poly([(-5,-116),(10,-120),(14,-116),(-4,-113)],INK),
                      poly([(27,-119),(39,-115),(39,-111),(27,-115)],INK),
                      poly([(15,-111),(13,-92),(24,-91),(25,-96),(20,-98)],shadow),
                      poly([(4,-75),(22,-77),(25,-73),(9,-72)],shadow)]
    initial=1 if profile else 0
    for is_profile,parts in [(True,profile_face),(False,front_face)]:
        a=initial if is_profile else 1-initial
        facegroup=group(parts)
        facegroup['transform']['alpha']=a
        if turn:
            facegroup['keys']=[dict(time=0,alpha=a),dict(time=2.5,alpha=a),dict(time=3.6,alpha=1-a),dict(time=6.5,alpha=1-a),dict(time=7.8,alpha=a),dict(time=18,alpha=a)]
        nodes.append(facegroup)
    if cap:
        cap_color=cap if isinstance(cap,str) else '#986b45'
        nodes.extend([poly([(-43,-140),(-39,-163),(-12,-179),(31,-180),(49,-164),(47,-145),(17,-143)],cap_color),
                      poly([(-47,-142),(25,-145),(48,-140),(15,-132),(-25,-132)],'#623c32' if isinstance(cap,str) else '#6c533d'),
                      poly([(-3,-159),(5,-170),(13,-160),(7,-149)],'#c8b56d')])
    if helmet:
        nodes.extend([poly([(-50,-129),(-47,-160),(-31,-185),(0,-196),(32,-187),(51,-165),(56,-134),(40,-124),(-36,-122)],'#59604a'),
                      poly([(-39,-159),(-20,-179),(4,-185),(28,-178),(39,-163),(8,-168)],'#7f8264'),
                      poly([(-49,-136),(53,-140),(58,-128),(43,-119),(-40,-119)],'#404c3c'),
                      line([(-36,-120),(-25,-66),(15,-51),(39,-114)],'#464537',4)])
    keys=None
    if turn:
        keys=[dict(time=0,rotation=-2),dict(time=2.5,rotation=-2),dict(time=3.6,rotation=4),dict(time=6.5,rotation=4),dict(time=7.8,rotation=0),dict(time=18,rotation=0)]
    return group(nodes,0,-380,keys=keys)


def arm(skin, coat, hand_pose='rest', bend=0, dark=None, keys=None):
    """Shoulder pivot: independently jointed upper arm, forearm and fingers."""
    shade=dark or MID
    sleeve=[poly([(-29,-5),(20,-7),(36,38),(33,121),(11,144),(-24,126),(-34,44)],coat),
            poly([(-29,10),(-8,12),(-4,109),(12,137),(-14,137),(-31,116)],shade),
            poly([(-17,114),(31,111),(31,130),(-14,137)],'#73786a')]
    palm=[poly([(-15,103),(18,102),(24,129),(19,158),(8,172),(-10,166),(-19,141)],skin),
          poly([(-14,116),(-25,128),(-24,149),(-13,148),(-6,134)],skin),
          line([(8,144),(10,162)],SKIN_D if skin!=BLACK_SKIN else BLACK_D,2),
          line([(0,148),(1,164)],SKIN_D if skin!=BLACK_SKIN else BLACK_D,2)]
    if hand_pose=='point':
        palm=[poly([(-14,101),(17,100),(23,123),(17,145),(-3,149),(-17,132)],skin),
              poly([(5,132),(18,135),(22,194),(12,201),(5,193)],skin),poly([(-13,117),(-25,130),(-21,147),(-11,138)],skin)]
    fore=group([poly([(-18,-5),(25,-4),(27,101),(16,116),(-15,112),(-23,26)],coat),
                poly([(-18,7),(-8,2),(-5,104),(-17,111),(-24,29)],shade),
                box(-18,91,43,18,'#9c9f85'),*palm],0,122,rotation=bend,keys=keys)
    return group([*sleeve,fore])


def rifle():
    return group([poly([(-12,-95),(9,-97),(15,77),(-2,88),(-13,56)],'#1b2829'),
                  poly([(-7,-230),(3,-230),(5,-96),(-6,-96)],'#253232'),
                  box(-9,-213,19,8,'#6b7163'),
                  poly([(1,-38),(20,-30),(26,8),(12,16),(4,-2)],'#202d2a'),
                  poly([(-11,62),(11,61),(16,117),(-7,130),(-19,102)],'#634f38'),
                  line([(-9,-70),(-49,0),(-38,116)],'#69715f',5)])


def person(x,y,scale=1,skin=SKIN,coat=OLIVE,cap=False,helmet=False,pose='rest',profile=False,turn=True,president=False,camouflage=False):
    shade='#373f35' if coat==OLIVE else ('#a89c7e' if president else '#263b3b')
    torso=[poly([(-29,-399),(24,-399),(41,-383),(91,-365),(114,-292),(104,-164),(-98,-164),(-113,-291),(-90,-366),(-48,-383)],coat),
           poly([(-97,-353),(-61,-366),(-55,-203),(-95,-164),(-108,-286)],shade),
           poly([(20,-396),(47,-380),(26,-336),(1,-363)],'#acaa8c' if president else '#7b8169'),
           poly([(-29,-397),(-48,-383),(-27,-337),(1,-362)],'#ded4b3' if president else '#87917a'),
           poly([(-10,-358),(3,-365),(15,-347),(5,-278),(-7,-282)],'#b4a777' if president else '#283a35'),
           box(-63,-312,49,39,shade),box(25,-312,48,39,shade),line([(-60,-308),(-18,-308)],'#9d9c7e',3),
           line([(29,-308),(68,-308)],'#9d9c7e',3),box(-83,-179,171,21,'#303b32'),
           box(-2,-176,22,16,'#a29365'),poly([(-84,-158),(-8,-158),(-15,-23),(-90,-18)],shade),
           poly([(-2,-158),(81,-157),(88,-19),(15,-20)],coat),
           poly([(-90,-23),(-17,-25),(-9,0),(-112,0),(-119,-12)],'#212a26'),
           poly([(17,-25),(86,-25),(112,-10),(110,0),(12,0)],'#1d2927')]
    larm=arm(skin,coat,dark=shade)
    rarm=arm(skin,coat,dark=shade)
    weapon=None
    if camouflage:
        torso.extend([poly([(-78,-354),(-40,-369),(-16,-343),(-21,-320),(-62,-320)],'#3d4b3b'),
                      poly([(17,-325),(44,-340),(80,-321),(76,-291),(39,-288)],'#7d7c56'),
                      poly([(-86,-273),(-51,-281),(-35,-248),(-46,-227),(-83,-236)],'#85845b'),
                      poly([(24,-252),(61,-263),(86,-235),(65,-204),(28,-214)],'#35473b'),
                      poly([(-27,-220),(-5,-234),(15,-219),(4,-190),(-26,-184)],'#797e5b'),
                      poly([(-88,-371),(-52,-385),(-42,-375),(-82,-358)],'#b04437'),
                      poly([(47,-383),(83,-372),(80,-360),(43,-371)],'#b04437')])
        for a in [larm,rarm]:
            a['children'].insert(2,poly([(-29,37),(-2,25),(19,39),(16,64),(-21,69)],'#7c7c57'))
            a['children'].insert(3,poly([(-15,78),(13,70),(31,90),(21,111),(-11,110)],'#35483a'))
    if pose in ('rifle','ready'):
        larm['transform']['rotation']=-16
        rarm['transform']['rotation']=27
        # Forearms rise to cradle the weapon.
        larm['children'][-1]['transform']['rotation']=-70
        rarm['children'][-1]['transform']['rotation']=59
        if pose=='rifle': weapon=group([rifle()],0,-265,rotation=82,scale=1.3)
    elif pose=='order':
        larm['transform']['rotation']=0
        rarm['transform']['rotation']=0
        larm['children'][-1]['transform']['rotation']=-70
        rarm['children'][-1]['transform']['rotation']=70
    elif pose=='gesture':
        rarm['transform']['rotation']=34
        rarm['children'][-1]['transform']['rotation']=65
        rarm['children'][-1]['keys']=[dict(time=0,rotation=63),dict(time=2.8,rotation=63),dict(time=4.2,rotation=39),dict(time=7,rotation=39),dict(time=9,rotation=63),dict(time=18,rotation=63)]
    if weapon is not None: torso.append(weapon)
    torso.extend([group([larm],-92,-359),group([rarm],91,-359),head(skin,profile=profile,turn=turn,cap=cap,helmet=helmet)])
    if president:
        torso.extend([poly([(33,-336),(66,-336),(67,-322),(34,-322)],'#bba25c'),
                      box(35,-331,6,7,'#885d41'),box(45,-331,6,7,'#735f42'),box(55,-331,6,7,'#64807a'),
                      poly([(-65,-386),(-35,-378),(-39,-368),(-71,-374)],'#bda156'),
                      poly([(37,-379),(71,-389),(78,-377),(43,-369)],'#bda156')])
    return group(torso,x,y,scale)


def car_interior(window_move=True):
    nodes=[box(0,0,1280,720,'#746e56'),
           poly([(0,37),(1280,0),(1280,389),(0,433)],'#adac88'),
           poly([(0,91),(1280,51),(1280,309),(0,359)],'#b3ad83'),
           poly([(0,277),(1280,231),(1280,351),(0,395)],'#8a9270')]
    landscape=[]
    for i in range(9):
        x=-100+i*185
        landscape.extend([poly([(x,192),(x+22,79),(x+116,82),(x+130,174),(x+169,200),(x+169,282),(x-24,290)],'#737f65'),
                          box(x+8,184,122,99,'#9b9976'),box(x+57,226,24,57,'#586d5f')])
    nodes.append(group(landscape,keys=[dict(time=0,x=0),dict(time=12,x=-185)],loop=12) if window_move else group(landscape))
    nodes.extend([poly([(0,0),(1280,0),(1280,77),(0,116)],'#293633'),
                  poly([(0,82),(39,90),(111,720),(0,720)],'#192a2b'),
                  poly([(922,18),(985,14),(1108,720),(1018,720)],'#243332'),
                  poly([(0,345),(1280,300),(1280,720),(0,720)],'#293a36'),
                  poly([(0,367),(1280,323),(1280,401),(0,442)],'#45544a'),
                  poly([(25,437),(1252,394),(1267,570),(42,615)],'#34463f'),
                  line([(38,453),(1247,414)],'#6b7360',4),
                  poly([(114,492),(239,489),(239,515),(113,526)],'#a39d7c'),
                  poly([(134,498),(229,498),(229,506),(133,513)],'#c0b18a'),
                  poly([(771,477),(912,470),(910,487),(774,494)],'#9b987a'),
                  poly([(898,450),(1279,429),(1280,720),(921,720)],'#17282a'),
                  poly([(924,458),(1279,440),(1280,480),(933,502)],'#3d4c43')])
    return nodes


def mazur_scene(kind='interior'):
    nodes=car_interior()
    # Passenger seated at a three-quarter angle, shoulder belt and detailed hand.
    hero=person(660,955,1.46,pose='ready',coat='#566452',profile=True,turn=True,camouflage=True)
    hero_head=next(n for n in hero['children'] if n.get('type')=='group' and n.get('transform',{}).get('y')==-380)
    # Scanning is a turn of the face contour. Keep his neck upright and his
    # shoulders square while the weapon remains ready across both hands.
    hero_head['keys']=[dict(time=0,rotation=0),dict(time=2.5,rotation=0),dict(time=3.6,rotation=1),dict(time=6.5,rotation=1),dict(time=7.8,rotation=0),dict(time=18,rotation=0)]
    hero['children'].extend([poly([(-89,-355),(-70,-368),(83,-164),(64,-162)],'#353e32'),
                             poly([(-83,-349),(-77,-354),(76,-166),(70,-164)],'#8b8d71')])
    nodes.append(hero)
    nodes.append(group([rifle()],674,570,rotation=78,scale=1.4))
    # Door frame occludes lower torso, fixing the body inside the car.
    nodes.extend([poly([(0,641),(1280,590),(1280,720),(0,720)],'#17292a'),
                  line([(0,640),(1280,590)],'#81836d',4)])
    if kind=='mirror':
        # A real mirror framed within the cabin, catching the two rear-seat guards.
        nodes.extend([poly([(65,88),(473,92),(486,250),(68,235)],'#1b2927'),
                      poly([(81,101),(457,105),(467,232),(83,220)],'#8f9b8b'),
                      poly([(92,163),(460,168),(466,231),(84,219)],'#344742')])
        for x,scale in [(169,.31),(350,.29)]:
            guard=person(x,286,scale,skin=SKIN,coat='#3b4f48',profile=(x>200),pose='rifle',turn=False,cap='#a74537',camouflage=True)
            nodes.append(guard)
        nodes.append(poly([(81,101),(457,105),(460,115),(84,112)],'#d7c8a1'))
    if kind=='alarm':
        # Snap of head and shoulders, not an impact or the current ambush.
        headnode=next(n for n in hero['children'] if n.get('type')=='group' and n.get('transform',{}).get('y')==-380)
        headnode['keys']=[dict(time=0,rotation=0),dict(time=1.0,rotation=0),dict(time=1.24,rotation=-4),dict(time=4,rotation=-4)]

        def turn_detail(detail):
            # Change the facing direction: nose, brow, ear, hair and cheek
            # facets all move to the opposite profile. Keep polygon winding
            # consistent so the interpolated face cannot collapse to a line.
            if detail['type']=='group':
                if 'alpha' in detail.get('transform',{}):
                    detail.pop('keys',None)
                for child in detail['children']: turn_detail(child)
            elif 'points' in detail:
                source=deepcopy(detail['points'])
                target=[[10-x,y] for x,y in source]
                if detail['type']=='polygon':
                    target.reverse()
                    rotations=[target[i:]+target[:i] for i in range(len(target))]
                    target=min(rotations,key=lambda pts:sum((a[0]-b[0])**2+(a[1]-b[1])**2 for a,b in zip(source,pts)))
                detail['keys']=[dict(time=0,points=source),dict(time=1,points=source),dict(time=1.24,points=target),dict(time=4,points=target)]

        for detail in headnode['children'][2:]: turn_detail(detail)
        hero['keys']=[dict(time=0,rotation=0,x=660),dict(time=1,rotation=0,x=660),dict(time=1.24,rotation=-2,x=647),dict(time=4,rotation=-2,x=647)]
    return scene(nodes,'#1d2d2c')


def eyes_scene():
    nodes=[box(0,0,1280,720,'#253632'),
           poly([(0,138),(1280,102),(1280,635),(0,680)],'#947861'),
           poly([(0,134),(540,104),(612,381),(450,665),(0,680)],'#705d4e'),
           poly([(462,110),(875,110),(1280,253),(1280,501),(777,630),(635,390)],'#b39678'),
           poly([(0,131),(82,20),(406,0),(757,38),(1133,6),(1280,78),(1280,244),(1015,171),(729,191),(528,163),(271,202),(0,253)],'#313b33'),
           poly([(95,282),(320,252),(505,279),(498,312),(297,288),(110,313)],'#414738'),
           poly([(763,263),(996,252),(1197,296),(1164,326),(990,291),(773,299)],'#424a3b'),
           poly([(100,345),(288,311),(485,341),(350,385),(190,378)],'#d4c8a8'),
           poly([(782,341),(993,313),(1172,351),(1061,391),(886,382)],'#d4c8a8'),
           poly([(490,317),(532,343),(517,388),(475,416),(468,393)],'#765c4b'),
           poly([(665,309),(720,342),(770,424),(689,448),(595,421)],'#896c55'),
           poly([(663,315),(680,374),(715,420),(664,425),(619,412)],'#c1a283'),
           poly([(15,456),(346,417),(509,440),(435,498),(85,514)],'#846d58'),
           poly([(827,432),(1081,424),(1254,477),(1197,521),(876,495)],'#9c8067')]
    for x,y in [(312,348),(992,352)]:
        nodes.append(group([ellipse(-30,-24,59,52,'#4a5145'),ellipse(-12,-19,25,42,'#1a2929'),poly([(-24,-22),(1,-24),(-4,-10),(-25,-9)],'#b7b99a')],x,y,keys=[dict(time=0,x=x),dict(time=2.5,x=x),dict(time=3.1,x=x-23),dict(time=8,x=x-23),dict(time=9,x=x+7),dict(time=18,x=x+7)]))
    return scene(nodes,'#243632')


def room():
    nodes=[box(0,0,1280,720,'#9f977c'),poly([(0,0),(1280,0),(1280,442),(0,412)],'#b2a88b'),
           poly([(0,0),(202,0),(201,427),(0,412)],'#8a8d75'),
           poly([(0,412),(1280,442),(1280,720),(0,720)],'#6d745f'),
           poly([(727,54),(1175,75),(1172,369),(730,346)],'#3a514b'),
           poly([(750,72),(1156,89),(1155,347),(749,329)],'#b2bca2'),
           poly([(750,257),(1156,248),(1155,347),(749,329)],'#788b72'),
           box(914,78,10,264,'#596d5b'),line([(750,191),(1155,196)],'#596d5b',9),
           poly([(105,81),(446,85),(445,327),(103,320)],'#716d50'),
           poly([(122,98),(427,100),(427,310),(120,305)],'#c3b98d'),
           poly([(153,267),(259,154),(304,181),(381,146),(405,286)],'#8d9672'),
           line([(153,267),(259,154),(304,181),(381,146)],'#a38f67',5),
           poly([(358,421),(1098,428),(1235,592),(219,571)],'#626d58'),
           poly([(219,571),(1235,592),(1234,631),(220,608)],'#3e5146'),
           poly([(270,608),(312,610),(294,720),(258,720)],'#3b4b40'),
           poly([(1125,630),(1167,629),(1193,720),(1153,720)],'#35473e')]
    return nodes


def appointment_scene():
    nodes=room()
    # A commander giving a concrete order while Mazur accepts it.
    nodes.append(person(406,711,.92,skin=SKIN,coat=OLIVE,cap='#a74537',pose='gesture',turn=True,camouflage=True))
    recipient=person(938,802,1.22,skin=SKIN,coat='#3c5149',pose='order',profile=False,turn=True,camouflage=True)
    paper=[poly([(-70,-48),(76,-38),(65,48),(-78,33)],PAPER),
           poly([(-57,-32),(15,-25),(13,-15),(-58,-20)],'#696f59')]
    for yy in [-5,3,11]: paper.append(line([(-59,yy-10),(48,yy-2)],'#90917a',2))
    paper.extend(round_seal(21,-4,19,'#9a6146'))
    paper.extend(round_seal(42,1,19,'#4a6780'))
    paper.append(flourish_signature(-57,11,115,23,width=1.1))
    paper_group=group(paper,0,-200,1.1,keys=[dict(time=0,rotation=3),dict(time=2,rotation=3),dict(time=4,rotation=-2),dict(time=18,rotation=-2)])
    first_arm=next(i for i,n in enumerate(recipient['children']) if n.get('transform',{}).get('y')==-359)
    recipient['children'].insert(first_arm,paper_group)
    nodes.append(recipient)
    return scene(nodes,'#99937b')


def order_scene(attempts=False):
    # A close view of the existing order: paper, lamp and the reader's hands.
    nodes=[box(0,0,1280,720,'#4b5d50'),
           poly([(0,0),(1280,0),(1280,720),(0,720)],'#58654e'),
           poly([(159,0),(871,0),(1115,720),(189,720)],'#788168'),
           poly([(0,594),(1280,657),(1280,720),(0,720)],'#33483e'),
           ellipse(907,77,228,72,'#3e5043'),ellipse(937,76,167,45,'#8d9271'),
           poly([(1009,0),(1037,0),(1030,92),(1003,92)],'#424c3b'),
           poly([(261,85),(858,111),(821,637),(231,584)],PAPER),
           poly([(269,92),(852,117),(851,133),(267,110)],'#ddd1b0'),
           poly([(791,587),(821,637),(777,626)],'#9a9479'),
           box(322,158,252,13,'#73755d'),box(322,185,339,8,'#a29e7e')]
    for i in range(9):
        y=235+i*24
        nodes.append(poly([(316,y),(737+(i%3)*17,y+16),(736+(i%3)*17,y+20),(315,y+4)],'#aba485'))
    nodes.extend(round_seal(643,419,77,'#9a6146'))
    nodes.extend(round_seal(728,448,77,'#4a6780'))
    nodes.append(flourish_signature(298,488,470,111,width=3.1))
    # Fingers press and release the page. Hands are independent of the paper.
    left=group([poly([(-147,171),(-119,20),(-80,-8),(-35,-16),(-2,2),(1,22),(-20,35),(-47,37),(-42,77),(-30,137),(-50,198)],SKIN),
                poly([(-119,20),(-108,67),(-67,118),(-49,182),(-59,196),(-91,150)],SKIN_D),
                poly([(-53,7),(-33,-11),(-3,-3),(11,15),(7,28),(-12,27)],SKIN_L),
                line([(-83,16),(-71,46),(-63,69)],SKIN_D,3),line([(-62,5),(-55,27)],SKIN_D,3),
                poly([(-158,167),(-43,179),(-25,268),(-178,266)],'#344b43'),
                poly([(-150,168),(-41,178),(-36,195),(-154,183)],'#8b9276')],266,417,keys=[dict(time=0,rotation=-4),dict(time=2,rotation=-4),dict(time=4,rotation=2),dict(time=18,rotation=2)])
    right=group([poly([(-37,201),(-42,90),(-32,39),(-7,1),(16,-14),(34,-1),(33,29),(17,57),(40,80),(58,133),(57,205)],SKIN),
                 poly([(20,13),(33,1),(33,29),(17,57),(40,80),(58,133),(57,205),(31,203),(25,118),(3,80)],SKIN_D),
                 poly([(-42,199),(61,197),(74,303),(-48,304)],'#3b5147'),
                 poly([(-44,193),(60,189),(63,209),(-44,214)],'#90967b'),
                 line([(-22,49),(-7,58),(3,76)],SKIN_D,3),line([(-6,15),(5,29)],SKIN_D,3)],862,436,keys=[dict(time=0,x=903,y=490,rotation=8),dict(time=2,x=903,y=490,rotation=8),dict(time=4.4,x=840,y=421,rotation=-9),dict(time=18,x=840,y=421,rotation=-9)])
    nodes.extend([left,right])
    if attempts:
        # Four holes through a vehicle body, concrete history rather than UI ticks.
        nodes=[box(0,0,1280,720,'#333f36'),poly([(0,0),(1280,0),(1280,720),(0,720)],'#61715f'),
               poly([(0,80),(1280,0),(1280,355),(0,448)],'#294443'),
               poly([(0,365),(1280,283),(1280,321),(0,405)],'#9a9c79'),
               poly([(0,424),(1280,342),(1280,720),(0,720)],'#65735b'),
               line([(140,0),(193,720)],'#3a5145',7),line([(1190,0),(1137,720)],'#3a5145',7)]
        for i,(x,y) in enumerate([(350,453),(491,495),(689,449),(839,511)]):
            nodes.extend([poly([(x-17,y-18),(x+2,y-27),(x+20,y-9),(x+16,y+15),(x-8,y+22),(x-23,y+7)],'#303f35',visible_from=i*1.6),
                          poly([(x-8,y-10),(x+5,y-15),(x+11,y),(x-1,y+12),(x-13,y+4)],'#142b2b',visible_from=i*1.6),
                          line([(x-24,y+17),(x-41,y+35)],'#90957a',3,visible_from=i*1.6),
                          line([(x+14,y-21),(x+30,y-38)],'#9e9d7c',3,visible_from=i*1.6)])
    return scene(nodes,'#4b5d50')


def security_scene(kind='portrait'):
    nodes=car_interior()
    if kind=='team':
        # Three bodies overlap in depth, hands already close to weapons.
        for x,y,s in [(284,875,1.15),(1020,907,1.2),(666,974,1.48)]:
            nodes.append(person(x,y,s,skin=SKIN,coat='#53604a',cap='#a74537',pose='rifle',turn=(x==666),camouflage=True))
    elif kind=='unknown':
        nodes.append(person(389,924,1.42,skin=SKIN,coat='#59644e',cap='#a74537',pose='rest',turn=False,camouflage=True))
        nodes.append(person(838,954,1.48,skin=SKIN,coat='#465a4c',cap='#a74537',pose='rifle',turn=True,camouflage=True))
        nodes.append(poly([(1086,0),(1280,0),(1280,720),(1040,720)],'#263e36'))
    else:
        guard=person(673,955,1.53,skin=SKIN,coat='#53604b',cap='#a74537',pose='rifle' if kind=='insignia' else 'gesture',turn=True,camouflage=True)
        nodes.append(guard)
        if kind=='insignia':
            # An actual near shoulder: the scarlet strap belongs to the cloth,
            # with a fastening button. No invented state crest or rank stars.
            nodes.extend([poly([(0,474),(208,358),(418,412),(497,720),(0,720)],'#667056'),
                          poly([(0,474),(131,404),(204,474),(175,586),(0,625)],'#344a3b'),
                          poly([(183,575),(326,483),(403,519),(431,632),(288,691)],'#8b885a'),
                          poly([(62,461),(199,383),(372,426),(350,478),(183,444),(88,496)],'#a44636'),
                          poly([(80,468),(199,400),(351,438),(342,458),(188,425),(94,481)],'#c05b43'),
                          ellipse(311,436,24,17,'#b9a467'),
                          line([(52,510),(186,456),(341,493)],'#485a43',5)])
    nodes.extend([poly([(0,660),(1280,605),(1280,720),(0,720)],'#1d332e'),line([(0,659),(1280,604)],'#82876d',4)])
    return scene(nodes,'#8c9177')


def president_scene(kind='window'):
    if kind=='window':
        # Tinted glass actually conceals the occupant: a glimpse, not revelation
        # of which limousine carries him.
        nodes=[box(0,0,1280,720,'#746f57'),poly([(0,0),(1280,0),(1280,720),(0,720)],'#74765e'),
               poly([(0,37),(1207,0),(1280,411),(0,481)],'#202f2b'),
               poly([(83,87),(1063,47),(1180,360),(39,423)],'#334b43'),
               poly([(35,456),(1280,386),(1280,720),(0,720)],'#263c35'),
               line([(0,452),(1280,381)],'#a09c78',9)]
        reflections=[]
        for x in [-300,40,380,720,1060]:
            reflections.append(poly([(x,78),(x+90,75),(x+338,394),(x+224,402)],'#82937c'))
        nodes.append(group(reflections,keys=[dict(time=0,x=0,alpha=.28),dict(time=12,x=-340,alpha=.28)],loop=12))
        nodes.extend([poly([(0,0),(1280,0),(1280,48),(0,98)],'#344637'),
                      poly([(1057,0),(1116,0),(1280,371),(1192,379)],'#43513c'),
                      poly([(0,481),(1280,411),(1280,720),(0,720)],'#344f42'),
                      poly([(918,531),(1122,522),(1147,547),(1139,564),(930,576)],'#b9ad85'),
                      poly([(936,539),(1110,531),(1125,543),(938,554)],'#495b46'),
                      line([(200,479),(222,720)],'#253e35',5)])
        return scene(nodes,'#53604e')
    helmet=(kind=='helmet')
    nodes=room() if not helmet else [box(0,0,1280,720,'#9c9c7c'),poly([(0,194),(1280,146),(1280,720),(0,720)],'#6c8066'),
        poly([(0,361),(164,257),(258,294),(412,206),(569,274),(746,214),(1010,288),(1280,180),(1280,720),(0,720)],'#4d6a58'),
        poly([(0,582),(1280,503),(1280,720),(0,720)],'#7b8060'),
        poly([(758,84),(1280,63),(1280,130),(738,149)],'#59664c'),
        poly([(755,142),(803,137),(768,720),(721,720)],'#3e5140'),
        poly([(1175,108),(1210,104),(1236,720),(1204,720)],'#3e5140')]
    # Background guards make the command position readable without UI labels.
    if helmet:
        nodes.append(person(273,725,.83,skin=BLACK_SKIN,coat=OLIVE,helmet=True,pose='rifle',turn=False))
        nodes.append(person(1128,695,.74,skin=BLACK_SKIN,coat=OLIVE,helmet=True,pose='rifle',turn=False))
    nodes.append(person(663,863,1.34,skin=BLACK_SKIN,coat=OLIVE if helmet else CREAM,helmet=helmet,cap=False,president=not helmet,pose='gesture',turn=True))
    if helmet:
        nodes.extend([poly([(0,641),(1123,576),(1280,631),(1280,720),(0,720)],'#465e48'),
                      poly([(157,624),(812,590),(925,647),(231,700)],'#aea783'),
                      line([(285,653),(450,620),(607,659),(782,616)],'#6e7e60',4),
                      poly([(1012,548),(1071,541),(1082,657),(1011,667)],'#273f34'),
                      poly([(1025,536),(1050,534),(1058,557),(1025,563)],'#7e8b69')])
    return scene(nodes,'#9c9c7c')


def escort_scene():
    nodes=car_interior()
    nodes.append(person(387,877,1.26,skin=BLACK_SKIN,coat='#2b4740',pose='rifle',turn=True))
    nodes.append(person(950,895,1.31,skin=BLACK_SKIN,coat='#485a45',pose='rifle',turn=False,profile=True))
    nodes.extend([poly([(0,656),(1280,600),(1280,720),(0,720)],'#1d322d'),line([(0,655),(1280,599)],'#7b846a',4)])
    return scene(nodes,'#1d332d')


def build_scenes():
    return {
        'security_team':security_scene('team'),
        'security_portrait':security_scene('portrait'),
        'security_insignia':security_scene('insignia'),
        'memory_appointment':appointment_scene(),
        'memory_attempts':order_scene(True),
        'president_window':president_scene('window'),
        'mazur_interior':mazur_scene('interior'),
        'mazur_mirror':mazur_scene('mirror'),
        'mazur_eyes':eyes_scene(),
        'resolve_president':president_scene('president'),
        'resolve_helmet':president_scene('helmet'),
        'resolve_alarm':mazur_scene('alarm'),
        'escort_interior':escort_scene(),
        # Detail shots remain reusable art, though not all have a director cue.
        'security_orders':order_scene(),
        'security_unknown':security_scene('unknown'),
    }


if __name__=='__main__':
    import json
    import sys
    print(json.dumps(build_scenes(),ensure_ascii=False,separators=(',',':')))
