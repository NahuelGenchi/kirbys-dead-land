#!/usr/bin/env python3
"""Run meaningful original-ROM bridge checks in private, headless instances.

Requires tools/oracle_build.py, tools/nds_build.py and the isolated ready2 seed.
No desktop input, game install, Steam save, or original ROM writes are used.
"""
import argparse,ctypes as C,json,subprocess,sys,struct
from nds_probe import Bridge,Proxy,bind_proxy_api,ROOT


def transform(b,ability):
    assert b.feed(ability)==-2, 'Idle Kirby must not accept consumed content'
    b.step(2,20);assert b.action_flags()&1
    assert b.feed(ability)==0;b.step(2,1);b.step(0,1);b.step(128,1);b.step(0,180)
    assert b.state()['ability']==ability


def run_case(name,clock=1):
    b=Bridge();bind_proxy_api(b)
    if clock!=1:
        # #150: the same case with the ARM9 at twice the DS clock (see --arm9-clock).
        b.lib.kssu_arm9_clock.argtypes=[C.c_void_p,C.c_uint32];assert b.lib.kssu_arm9_clock(b.handle,clock)==0
    try:
        if name=='scene_doors':
            registry=struct.unpack('<64H',b.read(0x0209fbec,128))
            assert registry[:4]==(1,11,396,132),registry[:4]
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)]
            b.lib.kssu_read_gpu.argtypes=[C.c_void_p,C.c_uint32,C.c_void_p,C.c_size_t]
            b.world(floor,394*65536,int(153.5*65536))
            b.step(0,120)
            assert all(struct.unpack('<H',b.read(0x0209fbec+i*8,2))[0]==0 for i in range(16))
            oam=C.create_string_buffer(1024)
            assert b.lib.kssu_read_gpu(b.handle,0x07000000,oam,1024)==0
            banks=[]
            for i in range(128):
                a0,a1,a2,_=struct.unpack_from('<4H',oam.raw,i*8)
                if a0&0x300!=0x200 and a0&255<192 and a1&511<256:banks.append(a2>>12)
            assert 0 in banks and 7 not in banks,banks
            assert b.lib.kssu_health(b.handle)==76
            return {'seed_door_center':[396,132],'stage_sparkle_records_disabled':16,'kirby_obj_preserved':True,'remaining_active_palette_banks':banks}
        if name.startswith('ability-'):
            ability=int(name.split('-')[1]);transform(b,ability);hits=set();poses=set();health=[]
            for i in range(75):
                state=b.step(2 if i<40 else 0);poses.add(state['graphic']&65535);health.append(b.lib.kssu_health(b.handle))
                for hit in b.hits():
                    if b.owns(hit['owner']) and hit['slot']!=14:
                        hits.add((hit['slot'],C.c_int16(hit['words'][2]).value))
            assert poses, 'Source ability did not run'
            assert min(health)==76, (ability,health)
            return {'ability':ability,'source_after':b.state()['ability'],'poses':sorted(poses),'hits':sorted(hits),'minimum_health':min(health)}
        if name=='far_shot_draw':
            # Charged Plasma's ball keeps flying past the DS screen. The original
            # OAM keeps its own culling; the bridge reports the culled pieces,
            # built with the original builder's rules from the same records.
            class Far(C.Structure):_fields_=[('x',C.c_int16),('y',C.c_int16),('a0',C.c_uint16),('a1',C.c_uint16),('a2',C.c_uint16),('order',C.c_uint16)]
            transform(b,12);floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)]
            b.lib.kssu_read_gpu.argtypes=[C.c_void_p,C.c_uint32,C.c_void_p,C.c_size_t]
            b.lib.kssu_far_objs.argtypes=[C.c_void_p,C.POINTER(Far),C.c_size_t]
            b.world(floor,(30*24+12)*65536,int(153.5*65536));oam=C.create_string_buffer(1024);far=(Far*256)();xs=[];frames=0;rom_x=set()
            for i in range(300):
                s=b.state();b.world(floor,s['x_raw'],s['y_raw'])
                b.step([64,0,128,0][i%4] if i<200 else (16,0,2,2)[i-200] if i<204 else 0)
                if i<204:continue
                s=b.state();n=b.lib.kssu_far_objs(b.handle,far,256);assert n>=0
                kirby=s['x_raw']//65536-s['camera_x'];frames+=n>0
                xs+=[f.x-kirby for f in far[:n] if f.a2>>12<=7]
                assert b.lib.kssu_read_gpu(b.handle,0x07000000,oam,1024)==0
                rom_x|={struct.unpack_from('<H',oam.raw,k*8+2)[0]&511 for k in range(128) if struct.unpack_from('<H',oam.raw,k*8)[0]!=0xf8}
            assert b.lib.kssu_read_gpu(b.handle,0x07000400,oam,1024)==0
            icon=any(struct.unpack_from('<H',oam.raw,k*8)[0]&255==4 and struct.unpack_from('<H',oam.raw,k*8+2)[0]&511==4 for k in range(128))
            assert xs and max(xs)>=380 and frames>=40,(max(xs,default=None),frames)
            assert not any(256<x<449 for x in rom_x),'Original OAM cull changed'
            assert icon,'Sub-screen ability icon missing'
            return {'farthest_culled_piece_from_kirby':max(xs),'frames_with_culled_pieces':frames,'original_oam_cull_unchanged':True,'sub_icon':icon}
        if name=='room_food':
            from oracle_snapshot import inspect_ram
            transform(b,24);floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)];q=Proxy()
            for i in range(500):
                state=b.state();b.world(floor,state['x_raw'],state['y_raw'])
                if not q.flags&12:b.lib.kssu_proxy_set(b.handle,1,75*65536,151*65536,100,True)
                b.step(2 if 5<=i<40 else 0);b.lib.kssu_proxy_state(b.handle,1,C.byref(q))
            before=inspect_ram(b.read(0x02000000,0x400000))['entities']
            foods=[e['address'] for e in before if e['kind_raw']==104];owned=[e['address'] for e in before if b.owns(e['address'])]
            assert len(foods)==1
            count=b.lib.kssu_room_cleanup(b.handle);assert count>=1
            b.step(0,60);after=inspect_ram(b.read(0x02000000,0x400000))['entities']
            assert not any(e['address'] in foods for e in after)
            assert all(any(e['address']==address for e in after) for address in owned)
            assert b.lib.kssu_room_cleanup(b.handle)==0
            return {'original_cook_food_retired':len(foods),'player_and_owned_effects_preserved':len(owned),'repeated_cleanup_safe':True}
        if name in ('proxy_birth_contact','proxy_birth_inhale'):
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)];q=Proxy();observed=[]
            b.world(floor,43*65536,int(153.5*65536));b.step(0,60)
            if name=='proxy_birth_inhale':b.step(2,20);assert b.action_flags()&1
            for i in range(45):
                state=b.state();b.world(floor,state['x_raw'],state['y_raw'])
                assert b.lib.kssu_proxy_set(b.handle,1,(75 if name=='proxy_birth_inhale' else 51)*65536,151*65536,100,True)==0
                assert b.lib.kssu_proxy_options(b.handle,1,18,True)==0
                assert b.lib.kssu_proxy_contact(b.handle,1,False)==0
                b.step(2 if name=='proxy_birth_inhale' else 0);b.lib.kssu_proxy_state(b.handle,1,C.byref(q));observed.append([q.flags,b.lib.kssu_health(b.handle)])
            assert all(not flags&(4|8|32|128) and hp==76 for flags,hp in observed),observed
            return {'frames':len(observed),'first_spawn_frames':observed[:4],'unwanted_capture_or_contact':False}
        if name in ('proxy_contact','proxy_contact_sword'):
            if name=='proxy_contact_sword':transform(b,18)
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)];q=Proxy();damage=[]
            b.world(floor,43*65536,int(153.5*65536));b.step(0,60)
            for i in range(110):
                state=b.state();b.world(floor,state['x_raw'],state['y_raw'])
                x=(70 if name=='proxy_contact_sword' else (85 if i<5 else 51))*65536
                b.lib.kssu_proxy_set(b.handle,1,x,151*65536,32760,True)
                assert b.lib.kssu_proxy_contact(b.handle,1,False)==0
                assert b.lib.kssu_proxy_options(b.handle,1,18,True)==0
                b.step(2 if name=='proxy_contact_sword' and 5<=i<35 else 0)
                b.lib.kssu_proxy_state(b.handle,1,C.byref(q))
                if q.flags&1 and not q.flags&16 and q.hp<32760:damage.append(32760-q.hp)
            assert b.lib.kssu_health(b.handle)==76
            if name=='proxy_contact_sword':assert damage==[18,16],damage
            else:
                b.lib.kssu_proxy_contact(b.handle,1,True)
                for i in range(5):
                    b.lib.kssu_proxy_set(b.handle,1,51*65536,151*65536,32760,True);b.step()
                assert b.lib.kssu_health(b.handle)==69
            return {'overlap_health':76,'original_attack_damage':damage,'reenabled_health':b.lib.kssu_health(b.handle)}
        if name=='proxy_touch_off':
            # A capturable enemy overlapping Kirby with original touch damage off:
            # neither is hurt; turned back on, the original touch costs Kirby 7 HP.
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)];q=Proxy()
            b.world(floor,43*65536,int(153.5*65536));b.step(0,60)
            for i in range(110):
                state=b.state();b.world(floor,state['x_raw'],state['y_raw'])
                b.lib.kssu_proxy_set(b.handle,1,(85 if i<5 else 51)*65536,151*65536,100,True)
                assert b.lib.kssu_proxy_options(b.handle,1,18,False)==0
                assert b.lib.kssu_proxy_contact(b.handle,1,False)==0
                b.step(0)
            b.lib.kssu_proxy_state(b.handle,1,C.byref(q))
            assert b.lib.kssu_health(b.handle)==76 and q.flags&1 and q.hp==100,(b.lib.kssu_health(b.handle),q.flags,q.hp)
            b.lib.kssu_proxy_contact(b.handle,1,True)
            for i in range(5):
                b.lib.kssu_proxy_set(b.handle,1,51*65536,151*65536,100,True);b.step()
            assert b.lib.kssu_health(b.handle)==69
            return {'overlap_health':76,'overlap_enemy_hp':100,'reenabled_health':69}
        if name in ('proxy_ice_detach','proxy_lethal'):
            transform(b,10 if name=='proxy_ice_detach' else 18)
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)];q=Proxy();hp=16 if name=='proxy_ice_detach' else 1;damage=[];dead_frame=None;gone_frame=None;frozen_frames=0
            for i in range(240):
                state=b.state();b.world(floor,state['x_raw'],state['y_raw'])
                if dead_frame is None:
                    assert b.lib.kssu_proxy_set(b.handle,1,75*65536,151*65536,hp,True)==0
                    b.lib.kssu_proxy_size(b.handle,1,24,34);b.lib.kssu_proxy_options(b.handle,1,18,False)
                b.step(2 if 5<=i<40 else 0);b.lib.kssu_proxy_state(b.handle,1,C.byref(q))
                if dead_frame is None and not q.flags&16 and q.hp<hp:
                    damage.append(hp-q.hp);hp=q.hp
                    if hp<=0:
                        dead_frame=i;assert b.lib.kssu_proxy_detach(b.handle,1)==0
                if q.entity:
                    callback=struct.unpack('<I',b.read(q.entity+0x30,4))[0]
                    frozen_frames+=callback==0x021583ec
                if q.flags&8:
                    gone_frame=i;assert dead_frame is not None, 'Source freed a lethal hit before reporting zero HP'
                    break
                if i==80 and name=='proxy_ice_detach':
                    path=ROOT/'oracle/ice-detached-test.state';b.save(path);assert b.lib.kssu_load_state(b.handle,bytes(path))==0
            assert dead_frame is not None and damage
            kicked_damage=[]
            if name=='proxy_ice_detach':
                assert frozen_frames>150 and gone_frame is None,(frozen_frames,gone_frame)
                target=Proxy();target_hp=100;start_x=q.x_raw
                for i in range(90):
                    state=b.state();b.world(floor,state['x_raw'],state['y_raw'])
                    b.lib.kssu_proxy_set(b.handle,2,160*65536,151*65536,target_hp,True)
                    b.step(16 if i<60 else 0);b.lib.kssu_proxy_state(b.handle,2,C.byref(target));b.lib.kssu_proxy_state(b.handle,1,C.byref(q))
                    if target.flags&1 and not target.flags&16 and target.hp<target_hp:
                        kicked_damage.append(target_hp-target.hp);target_hp=target.hp
                assert q.x_raw>start_x+200*65536 and kicked_damage==[38,38],kicked_damage
            else:assert gone_frame>dead_frame,(dead_frame,gone_frame)
            return {'native_damage_events':damage,'death_frame':dead_frame,'gone_frame':gone_frame,'frozen_frames':frozen_frames,'remaining_hp':q.hp,'kicked_ice_block_damage':kicked_damage}
        if name=='proxy_ice_release':
            # The runtime's Ice path (#42): the host freezes and kills the enemy
            # itself, so the lethal source Ice hit is retired at once with
            # active=false instead of being detached into the original block.
            from oracle_snapshot import inspect_ram
            def frozen_blocks():
                entities=inspect_ram(b.read(0x02000000,0x400000))['entities']
                return [e['address'] for e in entities if struct.unpack('<I',b.read(e['address']+0x30,4))[0]==0x021583ec]
            transform(b,10)
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)];q=Proxy();hp=16;damage=[];dead_frame=None;victim=None;frozen_frames=[]
            for i in range(120):
                state=b.state();b.world(floor,state['x_raw'],state['y_raw'])
                if dead_frame is None:
                    assert b.lib.kssu_proxy_set(b.handle,1,75*65536,151*65536,hp,True)==0
                    b.lib.kssu_proxy_size(b.handle,1,24,34);b.lib.kssu_proxy_options(b.handle,1,18,False)
                b.step(2 if 5<=i<40 else 0);b.lib.kssu_proxy_state(b.handle,1,C.byref(q))
                if dead_frame is None and not q.flags&16 and q.hp<hp:
                    damage.append(hp-q.hp);hp=q.hp
                    if hp<=0:
                        dead_frame=i;victim=q.entity;assert b.lib.kssu_proxy_set(b.handle,1,0,0,1,False)==0
                if dead_frame is not None and frozen_blocks():frozen_frames.append(i)
            assert dead_frame is not None and damage and victim
            # Acknowledged with active=false, the slot is unbound at once and the
            # original destructor removes the victim: no frozen block ever runs.
            assert q.flags==0 and frozen_frames==[],(q.flags,frozen_frames)
            assert victim not in [e['address'] for e in inspect_ram(b.read(0x02000000,0x400000))['entities']]
            # The slot is free again: a new enemy binds and is present.
            b.lib.kssu_proxy_set(b.handle,1,75*65536,151*65536,100,True);b.step(0,2);b.lib.kssu_proxy_state(b.handle,1,C.byref(q))
            assert q.flags&1 and not q.flags&8,q.flags
            return {'native_damage_events':damage,'death_frame':dead_frame,'frames_after_release':120-dead_frame-1,'frozen_block_frames':len(frozen_frames),'rebound_flags':q.flags}
        if name=='motion':
            inputs=[0]*100+[16]*20+[0]*30+[1]+[0]*70+[1]*5+[0]*8+[64]*60+[0]*30
            results=[]
            for rebase in [False,True]:
                assert b.lib.kssu_load_state(b.handle,bytes(ROOT/'oracle/ready2/000.state'))==0
                x=43*65536;y=int(153.5*65536);trace=[]
                for mask in inputs:
                    ox=x//(24*65536)-16 if rebase else 0;oy=y//(24*65536)-4 if rebase else 0
                    tx=x-ox*24*65536;ty=y-oy*24*65536
                    b.world([3 if row+oy>=7 else 0 for row in range(9) for col in range(100)],tx,ty)
                    state=b.step(mask);x+=state['x_raw']-tx;y+=state['y_raw']-ty
                    trace.append([x,y,state['vx_raw'],state['vy_raw'],state['graphic'],state['ability']])
                results.append(trace)
            assert [r[:4]+r[5:] for r in results[0]][60:]==[r[:4]+r[5:] for r in results[1]][60:], 'Rebasing changed source position, velocity or ability'
            assert [r[4] for r in results[0]][100:]==[r[4] for r in results[1]][100:], 'Rebasing changed source action poses'
            return {'matched_frames_after_settling':len(inputs)-60,'fields':['position','velocity','pose','ability']}
        if name in ('one_way','solid_ceiling'):
            block=131 if name=='one_way' else 3
            cells=[block if y==6 else 3 if y>=8 else 0 for y in range(9) for x in range(100)]
            b.world(cells,43*65536,int(177.5*65536))
            b.step(0,60);ys=[]
            for i in range(70):
                state=b.state();b.world(cells,state['x_raw'],state['y_raw']);ys.append(b.step(1 if i<20 else 0)['y_raw']/65536)
            if name=='one_way':assert min(ys)<129.5 and ys[-1]==129.5
            else:assert min(ys)==172.5 and ys[-1]==177.5, (min(ys),ys[-1])
            return {'minimum_y':min(ys),'rest_y':ys[-1]}
        if name=='ladder':
            floor=[134 if col==1 and row<7 else 3 if row>=7 else 0 for row in range(9) for col in range(100)]
            b.world(floor,36*65536,int(153.5*65536));b.step(0,60)
            high=b.step(64,30);still=b.step(0,30);low=b.step(128,30)
            assert high['y_raw']==still['y_raw'] and high['y_raw']<100*65536 and low['y_raw']==int(153.5*65536)
            return {'up_y':high['y_raw']/65536,'released_y':still['y_raw']/65536,'down_y':low['y_raw']/65536,'source_pose':high['graphic']&65535}
        if name=='ladder_top':
            # A vine ends under a solid block; exported as a one-way ladder top,
            # the original climbs into that cell instead of stopping below it.
            tops={}
            for top in (3,131):
                if top==131:b.close();b=Bridge()
                cells=[134 if col==1 and 2<=row<7 else top if col==1 and row==1 else 3 if row>=7 else 0 for row in range(9) for col in range(100)]
                b.world(cells,36*65536,int(153.5*65536));b.step(0,60)
                for i in range(150):
                    s=b.state();b.world(cells,s['x_raw'],s['y_raw']);s=b.step(64)
                tops[top]=s['y_raw']/65536
            assert tops[3]==52.5 and tops[131]==20.0, tops
            return {'solid_top_y':tops[3],'one_way_top_y':tops[131]}
        if name=='ladder_top_stomp':
            # Kirby stands on a vine's one-way top and presses Down: for its first
            # frames the original climb ignores A, so a stomp's single kick-off
            # press leaves him climbing (#74). Passing the top cell lets him go at once.
            action=lambda:struct.unpack('<H',b.read(0x020BAF80,2))[0]
            def cells(passed):return [134 if col==4 and 4<=row<8 else (0 if passed else 131) if col==4 and row==3 else 3 if row>=8 or (row==3 and col<10) else 0 for row in range(9) for col in range(100)]
            result={}
            for passed in (False,True):
                if passed:b.close();b=Bridge()
                b.world(cells(False),108*65536,int(57.5*65536));b.step(0,40)
                assert action()==0x406
                for i in range(3):s=b.state();b.world(cells(False),s['x_raw'],s['y_raw']);b.step(128)
                assert action()==0x916
                s=b.state();b.world(cells(passed),s['x_raw'],s['y_raw']);b.step(1);result[passed]=action()
            assert result[False]==0x916 and result[True]==0x70e, result
            return {'kick_in_top_cell':hex(result[False]),'top_cell_passed':hex(result[True])}
        if name in ('proxy_suplex','proxy_cook','proxy_suplex_touch_off'):
            ability=24 if name=='proxy_cook' else 6;transform(b,ability)
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)];q=Proxy();damage=[];captured=0;gone=None
            for i in range(380):
                s=b.state();b.world(floor,s['x_raw'],s['y_raw'])
                result=b.lib.kssu_proxy_set(b.handle,1,75*65536,151*65536,32760,True)
                if result==-3:break
                assert result==0
                if name.endswith('touch_off'):assert b.lib.kssu_proxy_contact(b.handle,1,False)==0
                b.step(2 if 5<=i<40 else (66 if 60<=i<80 else 0));b.lib.kssu_proxy_state(b.handle,1,C.byref(q))
                captured+=bool(q.flags&2)
                if q.flags&1 and not q.flags&16 and q.hp<32760:damage.append([i,32760-q.hp])
                if q.flags&8:gone=q.flags;break
            assert captured>30
            if ability==6:
                assert sum(v==50 for _,v in damage)==1, damage
            else:assert gone==136, (gone,damage)
            return {'native_capture_frames':captured,'damage_events':damage,'finished_flags':gone}
        if name=='health_invulnerable':
            transform(b,24);b.step(2)
            assert not any(h['slot']==14 and h['owner']==b.state()['player'] for h in b.hits()), 'Cook should unlink the active hurt node'
            assert b.lib.kssu_health(b.handle)==76 and b.lib.kssu_max_health(b.handle)==76
            assert b.lib.kssu_set_health(b.handle,50,76)==0
            assert b.lib.kssu_hurt(b.handle,10,1)==0
            health=[]
            for i in range(12):b.step();health.append(b.lib.kssu_health(b.handle))
            assert health==[50]*12,health
            return {'ability':'Cook','active_hurt_node_absent':True,'persistent_hp_access':50,'queued_damage_ignored_by_native_invulnerability':10}
        if name=='hurt_lethal':
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)]
            b.world(floor,43*65536,int(153.5*65536));b.step(0,60)
            assert b.lib.kssu_set_health(b.handle,3,76)==0
            assert b.lib.kssu_hurt(b.handle,10,1)==0
            observed=[]
            for i in range(6):
                b.step();hp=b.lib.kssu_health(b.handle);observed.append(hp)
                if hp==0:break
            assert observed[-1]==0,observed
            return {'original_lethal_damage':10,'initial_health':3,'reported_health':observed,'host_must_stop_source_at_zero':True}
        if name=='hurt_cold':
            before=b.lib.kssu_health(b.handle);assert b.lib.kssu_hurt(b.handle,7,1)==0
            observed=[b.lib.kssu_health(b.handle)]
            for i in range(6):b.step();observed.append(b.lib.kssu_health(b.handle))
            assert before==76 and observed[-1]==69, observed
            return {'cold_queued_hit_health':observed}
        if name=='water':
            b.world([130]*900,43*65536,70*65536);s=b.step(0,30)
            assert abs(s['vy_raw'])<200 and 140<=(s['graphic']&65535)<=180
            return {'source_pose':s['graphic']&65535,'source_vy_raw':s['vy_raw']}
        if name in ('proxy_water','proxy_water_sword'):
            # A host enemy standing in a pool: the original Waddle Dee's enter-water
            # defeat must not run, so the proxy stays capturable and hittable.
            sword=name=='proxy_water_sword'
            if sword:transform(b,18)
            pool=[3 if y>=7 else (130 if y in (5,6) and x>=3 else 0) for y in range(9) for x in range(100)];q=Proxy();seen=set();damage=[];swallowed=None
            b.world(pool,43*65536,int(153.5*65536));b.step(0,60)
            for i in range(150):
                state=b.state();b.world(pool,state['x_raw'],state['y_raw'])
                if not q.flags&12:assert b.lib.kssu_proxy_set(b.handle,1,(70 if sword else 85)*65536,151*65536,32760 if sword else 100,True)==0
                b.step(2 if i>=90 and (not sword or i<120) else 0);b.lib.kssu_proxy_state(b.handle,1,C.byref(q))
                if i<90:seen.add(q.flags)
                elif sword and q.flags&1 and not q.flags&16 and q.hp<32760:damage.append(32760-q.hp)
                if q.flags&4:swallowed=i-90;break
            assert not any(flags&(2|8) for flags in seen),seen
            if sword:assert damage==[18,16] and not q.flags&8,(damage,q.flags)
            else:assert swallowed is not None and b.action_flags()&2,(q.flags,swallowed)
            return {'idle_flags_in_water':sorted(seen),'original_inhale_frames':swallowed,'original_attack_damage':damage}
        if name=='proxy_water_below':
            # A pool under a walkway: Kirby's ledge floor is one row above the pool floor.
            cells=[3 if (x<3 and y>=7) or (x>=3 and y>=8) else (130 if x>=3 and y==7 else 0) for y in range(9) for x in range(100)];q=Proxy();seen=set()
            b.world(cells,43*65536,int(153.5*65536));b.step(0,60)
            for i in range(100):
                state=b.state();b.world(cells,state['x_raw'],state['y_raw'])
                assert b.lib.kssu_proxy_set(b.handle,1,85*65536,175*65536,100,True)==0
                b.step();b.lib.kssu_proxy_state(b.handle,1,C.byref(q));seen.add(q.flags)
            assert not any(flags&(2|8) for flags in seen) and q.hp==100,(seen,q.hp)
            return {'idle_flags_below_kirby':sorted(seen),'enemy_hp':q.hp}
        if name=='hurt_water':
            # The parked damage carrier must survive original water around Kirby.
            b.world([130]*900,43*65536,70*65536);b.step(0,60)
            health=[b.lib.kssu_health(b.handle)]
            for wait in (0,150):
                b.step(0,wait);assert b.lib.kssu_hurt(b.handle,7,1)==0;b.step(0,3);health.append(b.lib.kssu_health(b.handle))
            assert health==[76,69,62],health
            return {'swimming_health':health}
        if name=='mix_no_limited':
            # The Mix wheel never shows or gives a limited-use ability (#141): the stops that
            # gave Crash, Mike and Cook give the next face (Stone, Yo-Yo, Fighter).
            wheel=[11,2,7,7,13,18,15,8,16,36,4,6,25,14,14,12,20,10,3,19,5,5,17,32,9]
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)];seed=str(ROOT/'oracle/ready2/000.state').encode();given={};lap=None
            for press in (111,167,239):
                assert b.lib.kssu_load_state(b.handle,seed)==0
                qs=[Proxy(),Proxy()];previous=[0,0];faces=[]
                b.world(floor,43*65536,int(153.5*65536));b.step(0,60)
                for i in range(press+40):
                    state=b.state();b.world(floor,state['x_raw'],state['y_raw'])
                    for index in range(2):
                        if previous[index]&12:continue
                        assert b.lib.kssu_proxy_set(b.handle,index+1,(85+index*8)*65536,151*65536,32760,True)==0
                        assert b.lib.kssu_proxy_options(b.handle,index+1,18 if index==0 else 4,False)==0
                    b.step(2 if 5<=i<75 else (128 if i==90 else (2 if i==press else 0)))
                    for index in range(2):
                        b.lib.kssu_proxy_state(b.handle,index+1,C.byref(qs[index]));previous[index]=qs[index].flags
                    if 100<=i<=press:faces.append(b.read(0x020bb213,1)[0])
                assert list(b.read(0x020baa8c,25))==wheel
                assert not any(21<=face<=24 for face in faces),faces
                given[press]=b.state()['ability'];lap=[face for n,face in enumerate(faces) if n==0 or faces[n-1]!=face]
            assert given=={111:14,167:7,239:5},given
            # One full turn in the original order: the doubled faces appear once, for twice as long.
            turn=[face for n,face in enumerate(wheel) if wheel[n-1]!=face]
            start=turn.index(lap[1]);assert lap[1:1+len(turn)]==turn[start:]+turn[:start],lap
            return {'wheel':wheel,'ability_by_stop_frame':given,'faces_in_one_turn':len(turn)}
        if name in ('proxy_mix','boss_inhale','boss_suplex','boss_cook','boss_sword'):
            ability={'boss_suplex':6,'boss_cook':24,'boss_sword':18}.get(name,1)
            if ability!=1:transform(b,ability)
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)];qs=[Proxy(),Proxy()];previous=[0,0];damage=[];buffer=None
            b.world(floor,43*65536,int(153.5*65536));b.step(0,60)
            for i in range(260):
                state=b.state();b.world(floor,state['x_raw'],state['y_raw'])
                for index in range(2 if name=='proxy_mix' else 1):
                    if previous[index]&12:continue
                    assert b.lib.kssu_proxy_set(b.handle,index+1,(85+index*8 if ability==1 else 75)*65536,151*65536,32760,True)==0
                    assert b.lib.kssu_proxy_options(b.handle,index+1,18 if index==0 else 4,name!='proxy_mix')==0
                b.step(2 if 5<=i<75 else (128 if i==90 else (2 if i==200 else 0)))
                for index in range(2 if name=='proxy_mix' else 1):
                    b.lib.kssu_proxy_state(b.handle,index+1,C.byref(qs[index]));q=qs[index];previous[index]=q.flags
                    if q.flags&1 and not q.flags&16 and q.hp<32760:damage.append(32760-q.hp)
                if i==74:buffer=list(b.read(0x020bb210,8))
            if name=='proxy_mix':
                assert previous==[4,4] and buffer[1]==2 and buffer[3:5]==[18,4], (previous,buffer)
                assert 2<=b.state()['ability']<=25
                return {'original_mouth_buffer':buffer,'native_mix_selected_ability':b.state()['ability']}
            assert qs[0].flags&1 and not qs[0].flags&(4|8|32|128)
            if ability==18:assert damage==[18,16,18,16],damage
            else:assert not damage,damage
            return {'ability':ability,'native_damage_events':damage,'capture_rejected':True}
        if name=='proxy_capacity':
            capacity=b.lib.kssu_proxy_capacity();assert capacity==31
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)];q=Proxy()
            b.world(floor,384*65536,int(153.5*65536));b.step(0,60)
            for i in range(90):
                state=b.state();b.world(floor,state['x_raw'],state['y_raw'])
                for slot in range(1,capacity+1):
                    assert b.lib.kssu_proxy_set(b.handle,slot,(250+slot*9)*65536,151*65536,32760,True)==0
                    b.lib.kssu_proxy_options(b.handle,slot,18,True);b.lib.kssu_proxy_size(b.handle,slot,24,34)
                b.step(2 if i>=30 else 0)
            path=ROOT/'oracle/capacity-test.state';b.save(path);assert b.lib.kssu_load_state(b.handle,bytes(path))==0
            for slot in range(1,capacity+1):
                b.lib.kssu_proxy_state(b.handle,slot,C.byref(q));assert q.flags&1 and q.entity
                b.lib.kssu_proxy_set(b.handle,slot,0,0,0,False)
            b.step(0,4)
            for slot in range(1,capacity+1):b.lib.kssu_proxy_state(b.handle,slot,C.byref(q));assert q.flags==0
            return {'simultaneous_native_proxies':capacity,'save_reload_destroy':True}
        if name=='copy_drop_reabsorb':
            transform(b,18);floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)];dropped=False;consumed=False
            for i in range(180):
                state=b.state();b.world(floor,state['x_raw'],state['y_raw'])
                state=b.step(4 if i==0 else (2 if 30<=i<120 else (128 if i==125 else 0)))
                if i==0:dropped=state['ability']==1
                if 70<=i<120:consumed|=bool(b.action_flags()&2)
            assert dropped and consumed and b.state()['ability']==18
            return {'original_select_drop':True,'original_star_reinhale_swallow':18}
        if name=='wheelie_ride':
            transform(b,8);floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)]
            def helper_position():
                player=b.state()['player'];node=struct.unpack('<I',b.read(player,4))[0]
                while node:
                    data=b.read(node,0x88)
                    if data[0x16]==56:return struct.unpack_from('<ii',data,0x80)
                    node=struct.unpack_from('<I',data)[0]
                raise AssertionError('Original Wheelie helper missing')
            for i in range(151):
                state=b.state();b.world(floor,state['x_raw'],state['y_raw']);b.step(1024 if i==0 else 0)
            assert b.lib.kssu_room_cleanup(b.handle)==0
            mounted=[];abilities=set()
            for i in range(120):
                hx,hy=helper_position();state=b.state();x=state['x_raw']/65536;vx=state['vx_raw']/256;delta=hx/65536-x-vx*4
                mask=(16 if delta>1 else 32 if delta<-1 else 0)|(1 if i in (0,90) else 0)|(64 if i>=80 else 0)
                b.world(floor,state['x_raw'],state['y_raw']);state=b.step(mask);abilities.add(state['ability'])
                assert b.lib.kssu_health(b.handle)==76
                if state['graphic']&65535==65535:mounted.append([i,state['x_raw']/65536,state['y_raw']/65536])
            assert len(mounted)>30 and abilities=={1} and b.state()['graphic']&65535!=65535, (len(mounted),abilities,b.state()['graphic']&65535)
            assert mounted[-1][1]-mounted[0][1]>60
            return {'native_mounted_frames':len(mounted),'mounted_ability':1,'native_jump_dismount':True}
        if name in ('proxy_swallow','proxy_swallow_touch_off'):
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)]
            b.world(floor,43*65536,int(153.5*65536));b.step(0,60);controlled=0;q=Proxy()
            for i in range(60):
                s=b.state();b.world(floor,s['x_raw'],s['y_raw'])
                assert b.lib.kssu_proxy_set(b.handle,1,85*65536,151*65536,100,True)==0
                if name.endswith('touch_off'):assert b.lib.kssu_proxy_contact(b.handle,1,False)==0
                b.step(2 if i>=5 else 0);b.lib.kssu_proxy_state(b.handle,1,C.byref(q))
                controlled+=bool(q.flags&32)
                if q.flags&4:break
            assert q.flags&4 and controlled>=15 and b.action_flags()&2
            assert b.lib.kssu_set_swallowed_ability(b.handle,18)==0
            b.step(0,1);b.step(128,1);b.step(0,180);assert b.state()['ability']==18
            assert b.lib.kssu_load_state(b.handle,bytes(ROOT/'oracle/ready2/000.state'))==0
            assert b.state()['ability']==1
            return {'original_inhale_frames':controlled,'source_swallow_evolution':18,'in_place_reload':True}
        if name in ('hazard_spit','hazard_swallow'):
            # A host projectile: no copy byte, no body contact, a small hurtbox and
            # one HP. Its mouthful is acknowledged like the adapter does (released).
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)];shot,target=Proxy(),Proxy()
            b.world(floor,43*65536,int(153.5*65536));b.step(0,60)
            released=None;buffer=None;target_hp=32760;damage=[];health=[];spit=None;reinhale=False
            for i in range(240):
                state=b.state();b.world(floor,state['x_raw'],state['y_raw'])
                if released is None:
                    assert b.lib.kssu_proxy_set(b.handle,1,85*65536,140*65536,1,True)==0
                    assert b.lib.kssu_proxy_options(b.handle,1,0,False)==0
                    assert b.lib.kssu_proxy_contact(b.handle,1,False)==0
                    assert b.lib.kssu_proxy_size(b.handle,1,10,6)==0
                assert b.lib.kssu_proxy_set(b.handle,2,170*65536,151*65536,target_hp,True)==0
                assert b.lib.kssu_proxy_options(b.handle,2,18,True)==0
                mask=2 if 5<=i<60 else 0
                if released is not None and i==released+40:mask=2 if name=='hazard_spit' else 128;spit=i
                if name=='hazard_swallow' and released is not None and released+120<=i<released+140:mask=2
                b.step(mask);health.append(b.lib.kssu_health(b.handle))
                if name=='hazard_swallow' and released is not None and released+120<=i<released+140:reinhale|=bool(b.action_flags()&1)
                b.lib.kssu_proxy_state(b.handle,1,C.byref(shot));b.lib.kssu_proxy_state(b.handle,2,C.byref(target))
                if released is None and shot.flags&4:
                    buffer=list(b.read(0x020bb210,8));assert b.action_flags()&2
                    assert b.lib.kssu_proxy_set(b.handle,1,0,0,0,False)==0;released=i
                if target.flags&1 and not target.flags&16 and target.hp<target_hp:damage.append(target.hp-target_hp);target_hp=target.hp
            assert released is not None and buffer[1]==1 and buffer[3]==0, buffer
            assert min(health)==76 and b.state()['ability']==1, (min(health),b.state()['ability'])
            # Spitting clears the original mouth count. A copyless swallow returns
            # Kirby to base idle but leaves that count byte (action bit1) set until
            # the next inhale; B then inhales again instead of firing a star.
            if name=='hazard_spit':assert len(damage)==1 and damage[0]<0 and not b.action_flags()&2, damage
            else:assert not damage and reinhale, (damage,reinhale)
            return {'capture_frame':released,'original_mouth_buffer':buffer,'mouthful_action':'spit' if name=='hazard_spit' else 'swallow',
                    'action_frame':spit,'star_damage_events':[-d for d in damage],'ability_after':1,'minimum_health':76,
                    **({'next_b_inhales':reinhale} if name=='hazard_swallow' else {})}
        if name.startswith('star_'):
            # Kirby's original star objects, told apart by their update routine
            # (src/core/StarProjectiles.cs); boxes and damage come from the native
            # hit list, as the runtime reads them. Inclusive edges, like the source.
            routines={0x020bd718:'spit',0x020bdd08:'big',0x02166a54:'ability'}
            wall={'star_spit_wall':5,'star_ability':4}.get(name)
            floor=[3 if y>=7 or x==wall else 0 for y in range(9) for x in range(100)]
            overlap=lambda a,c:a[0]<=c[2] and c[0]<=a[2] and a[1]<=c[3] and c[1]<=a[3]
            def stars():
                found={}
                for h in b.hits():
                    o=h['owner']
                    if not 0x02000000<=o<0x023ffff0:continue
                    kind=routines.get(struct.unpack('<I',b.read(o+0x30,4))[0])
                    if not kind:continue
                    l,t,r,bottom=h['bounds'];s=found.setdefault(o,{'kind':kind,'box':[l,t,r,bottom],'damage':0})
                    s['box']=[min(s['box'][0],l),min(s['box'][1],t),max(s['box'][2],r),max(s['box'][3],bottom)]
                    s['damage']=max(s['damage'],-C.c_int16(h['words'][2]).value)
                return found
            def body(entity):
                boxes=[h['bounds'] for h in b.hits() if h['owner']==entity]
                return [min(x[0] for x in boxes),min(x[1] for x in boxes),max(x[2] for x in boxes),max(x[3] for x in boxes)] if boxes else None
            b.world(floor,43*65536,int(153.5*65536));b.step(0,60)
            if name=='star_ability':
                transform(b,18);press=4;target_x=60
            else:
                # Copyless host-projectile proxies, acknowledged like the adapter does.
                shots=[(1,85,140)] if name!='star_big' else [(1,80,151),(2,92,151)];caught=set();q=Proxy()
                for i in range(120):
                    state=b.state();b.world(floor,state['x_raw'],state['y_raw'])
                    for slot,x,y in shots:
                        if slot in caught:continue
                        b.lib.kssu_proxy_set(b.handle,slot,x*65536,y*65536,1,True);b.lib.kssu_proxy_options(b.handle,slot,0,False)
                        b.lib.kssu_proxy_contact(b.handle,slot,False);b.lib.kssu_proxy_size(b.handle,slot,10,6)
                    b.step(2 if 5<=i<70 and len(caught)<len(shots) else 0)
                    for slot,_,_ in shots:
                        b.lib.kssu_proxy_state(b.handle,slot,C.byref(q))
                        if slot not in caught and q.flags&4:caught.add(slot);b.lib.kssu_proxy_set(b.handle,slot,0,0,0,False)
                assert len(caught)==len(shots) and b.action_flags()&2,(caught,b.action_flags())
                press=2;target_x=None if name=='star_spit_wall' else 170
            target=Proxy();target_hp=32760;damage=[];trace=[]
            for i in range(240):
                state=b.state();b.world(floor,state['x_raw'],state['y_raw'])
                if target_x is not None:
                    b.lib.kssu_proxy_set(b.handle,3,target_x*65536,151*65536,target_hp,True);b.lib.kssu_proxy_options(b.handle,3,18,True)
                    b.lib.kssu_proxy_contact(b.handle,3,False);b.lib.kssu_proxy_size(b.handle,3,24,34)
                b.step(press if i<4 else 0);found=stars()
                assert len(found)<=1,found
                star=next(iter(found.values()),None);hit=False
                if target_x is not None:
                    b.lib.kssu_proxy_state(b.handle,3,C.byref(target))
                    if target.flags&1 and not target.flags&16 and target.hp<target_hp:
                        hit=True;damage.append({'frame':i,'damage':target_hp-target.hp,'star_touching':bool(star) and overlap(star['box'],body(target.entity))})
                        target_hp=target.hp
                trace.append(star and {'frame':i,**star,'touching':target_x is not None and target.entity!=0 and body(target.entity) is not None and overlap(star['box'],body(target.entity))})
                assert b.lib.kssu_health(b.handle)==76
            seen=[s for s in trace if s];assert seen,'No original star appeared'
            kinds={s['kind'] for s in seen};last=max(s['frame'] for s in seen)
            if name in ('star_spit','star_spit_wall'):
                assert kinds=={'spit'} and {s['damage'] for s in seen}=={52},(kinds,{s['damage'] for s in seen})
            if name=='star_spit':
                # The single spit star bursts on its first enemy, in the frame of its hit.
                assert len(damage)==1 and damage[0]['damage']==52 and damage[0]['star_touching'],damage
                assert last<=damage[0]['frame']+2,(last,damage)
                return {'routine':'0x020BD718','damage_word':52,'hits':damage,'last_star_frame':last,'bursts_on_enemy':True}
            if name=='star_spit_wall':
                # The wall cell starts at x=120: the star bursts when its center
                # reaches it, instead of bouncing back.
                centers=[(s['box'][0]+s['box'][2])/2 for s in seen]
                assert max(centers)<=120 and centers==sorted(centers) and last<60,(max(centers),last)
                return {'routine':'0x020BD718','wall_left':120,'furthest_center':max(centers),'last_star_frame':last,'bounced':False}
            if name=='star_big':
                assert kinds=={'big'} and {s['damage'] for s in seen}=={60},(kinds,{s['damage'] for s in seen})
                assert damage and damage[0]['damage']==60 and damage[0]['star_touching'],damage
                after=[s for s in seen if s['frame']>damage[0]['frame']+2]
                assert after and after[-1]['box'][0]>body(target.entity)[2],'The big star must pass through the enemy'
                return {'routine':'0x020BDD08','damage_word':60,'first_hit':damage[0],'hits':len(damage),'passes_through':True}
            assert kinds=={'ability'} and {s['damage'] for s in seen}=={0},(kinds,{s['damage'] for s in seen})
            # Harmless to enemies in the ROM; bounces off the wall cell starting at x=96.
            touching=[s['frame'] for s in seen if s['touching']]
            assert touching and not damage,(touching,damage)
            centers=[(s['box'][0]+s['box'][2])/2 for s in seen];rights=[s['box'][2] for s in seen]
            turns=[seen[k]['box'][2] for k in range(1,len(seen)-1) if centers[k]>centers[k-1] and centers[k+1]<centers[k]]
            assert max(rights)<=96 and any(r>=94 for r in turns),(max(rights),turns)
            return {'routine':'0x02166A54','damage_word':0,'frames_touching_enemy':len(touching),'enemy_damage':0,
                    'wall_left':96,'bounce_right_edges':turns,'still_present_at':last}
        if name=='proxy_reuse':
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)];q=Proxy();entities=[]
            for cycle in range(8):
                for i in range(5):
                    state=b.state();b.world(floor,state['x_raw'],state['y_raw'])
                    b.lib.kssu_proxy_set(b.handle,1,85*65536,151*65536,100,True);b.step()
                b.lib.kssu_proxy_state(b.handle,1,C.byref(q));assert q.flags==1 and q.hp==100
                entities.append(q.entity)
                b.lib.kssu_proxy_set(b.handle,1,0,0,0,False);b.step(0,2)
                b.lib.kssu_proxy_state(b.handle,1,C.byref(q));assert q.flags==0 and q.entity==0
            return {'native_destroy_and_respawn_cycles':len(entities)}
        if name=='proxy_save':
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)];q=Proxy()
            b.world(floor,43*65536,int(153.5*65536));b.step(0,60)
            for i in range(16):
                state=b.state();b.world(floor,state['x_raw'],state['y_raw']);b.lib.kssu_proxy_set(b.handle,1,85*65536,151*65536,100,True);b.step(2 if i>=5 else 0)
            b.lib.kssu_proxy_state(b.handle,1,C.byref(q));assert q.flags&32
            path=ROOT/'oracle/proxy-save-test.state';b.save(path)
            traces=[]
            for branch in range(2):
                if branch:assert b.lib.kssu_load_state(b.handle,bytes(path))==0
                trace=[]
                for i in range(30):
                    b.step(2);b.lib.kssu_proxy_state(b.handle,1,C.byref(q));trace.append([q.flags,q.x_raw,q.y_raw,b.action_flags()])
                traces.append(trace)
            assert traces[0]==traces[1]
            return {'save_during_original_suction_frames_identical':30}
        if name=='audio':
            b.lib.kssu_read_audio.argtypes=[C.c_void_p,C.POINTER(C.c_int16),C.c_size_t];out=(C.c_int16*4096)();total=0
            for i in range(60):
                b.step();total+=b.lib.kssu_read_audio(b.handle,out,4096)//2
            assert 48000<total<48400
            return {'stereo_samples_per_60_ds_frames':total,'output_rate':48000}
        if name in ('hurt','guard','health_invulnerable','health_sync'):
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)]
            b.world(floor,43*65536,int(153.5*65536));b.step(0,60)
            initial=b.lib.kssu_health(b.handle);assert initial==b.lib.kssu_max_health(b.handle)==76
            if name=='health_sync':
                assert b.lib.kssu_set_health(b.handle,50,100)==0;b.step(0,20)
                assert b.lib.kssu_health(b.handle)==50 and b.lib.kssu_max_health(b.handle)==100
                return {'native_health':50,'native_max_health':100}
            if name=='guard':b.step(512,10)
            assert b.lib.kssu_hurt(b.handle,7,1)==0;b.step(512 if name=='guard' else 0)
            first=b.lib.kssu_health(b.handle)
            b.step(0,9);assert b.lib.kssu_hurt(b.handle,7,1)==0;b.step();second=b.lib.kssu_health(b.handle)
            if name=='hurt':assert first==69 and second==69
            else:assert initial-first<7
            b.step(0,120);assert b.lib.kssu_hurt(b.handle,7,1)==0;b.step();third=b.lib.kssu_health(b.handle)
            if name=='hurt':assert third==62
            return {'before':initial,'first_hit':first,'hit_during_native_immunity':second,'hit_after_immunity':third}
        if name=='proxy_size':
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)];q=Proxy()
            for i in range(40):
                state=b.state();b.world(floor,state['x_raw'],state['y_raw']);b.lib.kssu_proxy_set(b.handle,1,120*65536,151*65536,100,True)
                assert b.lib.kssu_proxy_size(b.handle,1,40,60)==0;b.step();b.lib.kssu_proxy_state(b.handle,1,C.byref(q))
                if i>2:
                    hits=[h for h in b.hits() if h['owner']==q.entity and h['slot']==30];assert len(hits)==1
                    box=hits[0]['bounds'];assert box[2]-box[0]==40 and box[3]-box[1]==60
                    assert abs(box[3]-(q.y_raw/65536+17))<1, (box,q.y_raw/65536)
            return {'native_hurtbox_size':[40,60],'matched_frames':37}
        if name=='proxy_sword':
            transform(b,18);floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)];hp=100;q=Proxy();damage=[]
            for i in range(50):
                s=b.state();b.world(floor,s['x_raw'],s['y_raw'])
                status=b.lib.kssu_proxy_set(b.handle,1,70*65536,151*65536,hp,True)
                if status==-3:break
                assert status==0;b.step(2 if 5<=i<35 else 0);b.lib.kssu_proxy_state(b.handle,1,C.byref(q))
                if q.flags&1 and not q.flags&16 and q.hp<hp:
                    damage.append(hp-q.hp);hp=q.hp
            assert damage==[18,16], f'Source Sword combo damage/debounce changed: {damage}'
            return {'original_collision_damage_events':damage,'remaining_native_hp':hp}
        if name in ('f_slot','f_slot_save'):
            # #102: the stored ability through the original SetAbility routine, never the swallow.
            l=b.lib
            for fn in ('kssu_set_ability','kssu_set_mike_shouts'):getattr(l,fn).argtypes=[C.c_void_p,C.c_uint8]
            l.kssu_ability_change_pending.argtypes=[C.c_void_p]
            ability=lambda:b.read(0x020baf7b,1)[0]
            def change(target,limit=20):
                assert l.kssu_set_ability(b.handle,target)==0
                for frame in range(1,limit+1):
                    b.step()
                    if not l.kssu_ability_change_pending(b.handle):
                        assert ability()==target,(ability(),target);return frame
                raise AssertionError('ability change never returned')
            transform(b,18)
            b.step(2,2)  # an inhale-free Sword swing is an attack: the original refuses a change
            refused=l.kssu_set_ability(b.handle,23);assert refused==-2,refused
            b.step(0,60)
            if name=='f_slot_save':
                import tempfile,os
                # Mike is still loading when the first frame ends; Crash has returned by then.
                assert l.kssu_set_ability(b.handle,22)==0;b.step()
                assert l.kssu_ability_change_pending(b.handle)==1
                path=os.path.join(tempfile.mkdtemp(),'mid.state').encode();b.save(path)
                assert l.kssu_load_state(b.handle,path)==0;assert l.kssu_ability_change_pending(b.handle)==1
                for _ in range(20):
                    b.step()
                    if not l.kssu_ability_change_pending(b.handle):break
                assert ability()==22 and b.action_flags()&1==0 and l.kssu_set_mike_shouts(b.handle,1)==0
                b.step(2,3)
                for frame in range(400):
                    b.step()
                    if ability()==1:break
                assert ability()==1
                return {'resumed_after_mid_change_save':True,'reverted_after':frame+4}
            switch=change(23)
            b.step(2,3);revert=None
            for frame in range(4,400):
                b.step()
                if ability()==1:revert=frame;break
            # Crash returns within its first frame and reverts 268 frames after it was armed.
            # A frame that ended 71 scanlines after VBlank saw both a frame apart (2 and 266, #134).
            assert (switch,revert)==(1,267),(switch,revert)
            restore=change(18)
            hit=False
            for frame in range(20):
                b.step(2 if frame<3 else 0)
                hit=hit or any(b.owns(h['owner']) and h['slot']!=14 for h in b.hits())
            assert ability()==18 and hit
            b.step(0,60)
            mike=change(22);assert l.kssu_set_mike_shouts(b.handle,1)==0 and b.read(0x0205b6b1,1)[0]==1
            b.step(2,3)
            for frame in range(4,400):
                b.step()
                if ability()==1:break
            assert ability()==1 and frame<200,frame
            assert l.kssu_set_mike_shouts(b.handle,1)==-2
            return {'refused_during_attack':refused,'switch_frames':switch,'crash_revert_frame':revert,'restore_frames':restore,'mike_one_shout_revert':frame}
        if name=='move_mouthful':
            # #158: player field 4 (record +0x14), which the original's move selectors and its
            # give-ability routine read, is 2 exactly while Kirby holds a mouthful: from the
            # stand after the catch, through the mouthful moves, until a spit's star leaves or a
            # swallowed copy has loaded. The mouth count behind kssu_action_flags bit 1 is set
            # from the catch and stays set after a swallow. A change is refused in a mouthful.
            class Move(C.Structure):_fields_=[('action',C.c_uint16),('carry',C.c_uint8),('ability',C.c_uint8),('descriptor',C.c_uint32),('table',C.c_uint32)]
            l=b.lib;l.kssu_move.argtypes=[C.c_void_p,C.POINTER(Move)]
            l.kssu_set_ability.argtypes=[C.c_void_p,C.c_uint8];l.kssu_ability_change_pending.argtypes=[C.c_void_p]
            l.kssu_proxy_options.argtypes=[C.c_void_p,C.c_uint32,C.c_uint8,C.c_bool]
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)];m=Move()
            def run(mask,frames):
                seen=[]
                for _ in range(frames):
                    at=b.state();b.world(floor,at['x_raw'],at['y_raw']);b.step(mask)
                    assert l.kssu_move(b.handle,C.byref(m))==0
                    v=(m.action,m.carry,m.ability,m.descriptor,b.action_flags()&2)
                    if not seen or seen[-1]!=v:seen.append(v)
                return seen
            def catch(slot):
                at=b.state();l.kssu_proxy_set(b.handle,slot,((at['x_raw']>>16)+40)*65536,151*65536,100,True);l.kssu_proxy_options(b.handle,slot,18,False)
                for _ in range(90):
                    run(2,1)
                    if b.action_flags()&2:break
                assert b.action_flags()&2
                return run(0,20)+run(16,30)+run(0,10)+run(1,10)+run(0,60)
            b.world(floor,384*65536,int(153.5*65536));plain=run(0,60)[-1]
            assert plain==(0x0406,1,1,0x021314d8,0),plain
            held=catch(1)
            mouthful={v[3] for v in held if v[1]==2}
            assert {0x02131508,0x02131148,0x021311c0,0x02131238,0x021310e8}<=mouthful,sorted(map(hex,mouthful))
            assert all(v[4] for v in held) and held[-1][:2]==(0x0406,2),held[-1]
            refused=l.kssu_set_ability(b.handle,23);assert refused==-2,refused
            spit=run(2,3)+run(0,80)
            assert spit[-1]==(0x0406,1,1,0x021314d8,0),spit[-1]
            catch(2)
            swallow=run(128,2)+run(0,200)
            loaded=next(i for i,v in enumerate(swallow) if v[2]==18)
            assert all(v[1]==2 for v in swallow[:loaded]),swallow[:loaded]
            assert swallow[-1][1:3]==(1,18) and swallow[-1][4]==2,swallow[-1]
            assert l.kssu_set_ability(b.handle,23)==0
            for _ in range(20):
                run(0,1)
                if not l.kssu_ability_change_pending(b.handle):break
            assert b.read(0x020baf7b,1)[0]==23
            return {'plain_stand':'%08x'%plain[3],'mouthful_moves':sorted('%08x'%d for d in mouthful),'change_in_mouthful':refused,
                    'after_spit':{'carry':spit[-1][1],'mouth_count':spit[-1][4]},
                    'after_swallow':{'carry':swallow[-1][1],'ability':swallow[-1][2],'mouth_count_left_set':bool(swallow[-1][4])},
                    'change_after_swallow':23}
        if name=='f_slot_plasma':
            # #135: Plasma's charge aura (effect type 20, update 0x020C838C) after an F press and back.
            l=b.lib;l.kssu_set_ability.argtypes=[C.c_void_p,C.c_uint8];l.kssu_ability_change_pending.argtypes=[C.c_void_p]
            l.kssu_read_gpu.argtypes=[C.c_void_p,C.c_uint32,C.c_void_p,C.c_size_t]
            ability=lambda:b.read(0x020baf7b,1)[0]
            def auras():
                ram=b.read(0x02000000,0x400000);actor=struct.unpack_from('<I',ram,0x49df0)[0];count=0
                while actor:
                    p=actor-0x02000000;count+=struct.unpack_from('<I',ram,p+0x30)[0]==0x020c838c;actor=struct.unpack_from('<I',ram,p)[0]
                return count
            def change(target):
                for _ in range(600):  # refused until Kirby is free again after the stored attack
                    if l.kssu_set_ability(b.handle,target)==0:break
                    b.step()
                else:raise AssertionError('ability change stayed refused')
                for _ in range(20):
                    b.step()
                    if not l.kssu_ability_change_pending(b.handle):
                        assert ability()==target,(ability(),target);return
                raise AssertionError('ability change never returned')
            def charge():
                # Direction taps build Plasma's charge; a stray aura drew OBJ pieces of the prohibited shape 3.
                invalid=0;oam=C.create_string_buffer(1024)
                for frame in range(120):
                    b.step(32 if frame%6<2 else 0)
                    assert l.kssu_read_gpu(b.handle,0x07000000,oam,1024)==0
                    words=[struct.unpack_from('<H',oam.raw,i*8)[0] for i in range(128)]
                    invalid+=any(w&0x300!=0x200 and w>>14==3 for w in words)
                return invalid
            transform(b,12);b.step(0,60)
            assert auras()==1 and charge()==0
            after={}
            for stored in (23,21):
                b.step(0,120);change(stored);b.step(2,3)
                for frame in range(4,1200):
                    b.step()
                    if ability()==1:break
                assert ability()==1 and auras()==0,(stored,ability(),auras())
                change(12);b.step(0,60)
                assert auras()==1,(stored,auras())
                invalid=charge();assert invalid==0 and auras()==1,(stored,invalid,auras())
                after[stored]={'auras':auras(),'invalid_obj_frames':invalid}
            return {'plasma_after_stored_ability':after}
        if name in ('helper_reaction','helper_move','helper_owns'):
            # The original CPU helper (DS X). The bridge caps its reaction wait and can move it alone.
            class Helper(C.Structure):_fields_=[('x_raw',C.c_int32),('y_raw',C.c_int32),('vx_raw',C.c_int16),('vy_raw',C.c_int16),('action',C.c_uint16),('reserved',C.c_uint16)]
            b.lib.kssu_helper_state.argtypes=[C.c_void_p,C.POINTER(Helper)];b.lib.kssu_move_helper.argtypes=[C.c_void_p,C.c_int32,C.c_int32]
            def helper():
                h=Helper();return h if b.lib.kssu_helper_state(b.handle,C.byref(h))==0 else None
            transform(b,18);floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)]
            b.world(floor,(50*24+12)*65536,int(153.5*65536));b.step(0,30)
            assert helper() is None, 'No helper before the X button'
            for i in range(200):
                state=b.state();b.world(floor,state['x_raw'],state['y_raw']);b.step(1024 if i==0 else 0)
            h=helper();assert h is not None, 'Original helper missing'
            assert abs(h.x_raw-b.state()['x_raw'])<24*65536 and h.action in (0x0406,0x142c), (h.x_raw,h.action)
            getter=struct.unpack('<2I',b.read(0x020b53d4,8));assert getter==(0xe3500006,0xc3a00006),[hex(w) for w in getter]
            if name=='helper_reaction':
                # Without the cap an enemy 110 px away was first hit about 185 frames after it appeared (60 px: 30).
                frames={};q=Proxy()
                for distance in (60,110):
                    hit=None;b.lib.kssu_proxy_set(b.handle,1,0,0,100,False);b.step(0,60)
                    for i in range(240):
                        state=b.state();b.world(floor,state['x_raw'],state['y_raw'])
                        b.lib.kssu_proxy_state(b.handle,1,C.byref(q))
                        if i==0 or not q.flags&12:b.lib.kssu_proxy_set(b.handle,1,int((state['x_raw']/65536+distance)*65536),151*65536,100,True)
                        b.step(0);b.lib.kssu_proxy_state(b.handle,1,C.byref(q))
                        if q.hp<100:hit=i;break
                    assert hit is not None and hit<=60,(distance,hit)
                    frames[distance]=hit
                return {'cap_words':[hex(w) for w in getter],'first_hit_frames_by_distance':frames}
            if name=='helper_owns':
                # The helper and what it attacks with are told apart from Kirby's (#128): kssu_player_owns counts both.
                b.lib.kssu_helper_owns.argtypes=[C.c_void_p,C.c_uint32]
                def helper_nodes():
                    found=[]
                    for slot in range(7):
                        a=struct.unpack('<I',b.read(0x02049dec+slot*4,4))[0];seen=set()
                        while a and a not in seen and len(seen)<256:
                            seen.add(a);node=b.read(a,0x44)
                            if node[0x16]==56 and struct.unpack('<I',node[0x40:0x44])[0]==0x020bb784:found.append(a)
                            a=struct.unpack('<I',node[0:4])[0]
                    return found
                nodes=helper_nodes();assert len(nodes)==1,nodes
                player=b.state()['player']
                assert b.lib.kssu_helper_owns(b.handle,nodes[0])==1,'The helper owns itself'
                assert b.lib.kssu_helper_owns(b.handle,player)==0,'Kirby is not the helper'
                assert b.lib.kssu_helper_owns(b.handle,0)==0 and b.lib.kssu_helper_owns(b.handle,0x12345678)==0
                assert b.owns(player) and b.owns(nodes[0]),'kssu_player_owns still counts both'
                # The helper's attack records, with an enemy where it will hit.
                q=Proxy();helper_attack=kirby_attack=0;hit=None
                b.lib.kssu_proxy_set(b.handle,1,0,0,100,False);b.step(0,60)
                for i in range(240):
                    state=b.state();b.world(floor,state['x_raw'],state['y_raw'])
                    b.lib.kssu_proxy_state(b.handle,1,C.byref(q))
                    if i==0 or not q.flags&12:b.lib.kssu_proxy_set(b.handle,1,int((state['x_raw']/65536+60)*65536),151*65536,100,True)
                    b.step(0)
                    for record in b.hits():
                        # Slot 14 is a body (damage word 2 for Kirby and the helper alike), not an attack.
                        if record['slot']==14 or -struct.unpack('<h',struct.pack('<H',record['words'][2]))[0]<=0 or not b.owns(record['owner']):continue
                        if b.lib.kssu_helper_owns(b.handle,record['owner'])==1:helper_attack+=1
                        else:kirby_attack+=1
                    b.lib.kssu_proxy_state(b.handle,1,C.byref(q))
                    if q.hp<100:hit=i;break
                assert hit is not None and helper_attack>0 and kirby_attack==0,(hit,helper_attack,kirby_attack)
                # Kirby's own attack: his records are owned, damaging and never the helper's.
                # Making the helper took his copy, so he copies Sword again; his Sword and the
                # helper's both sit in slot 7, so only the owner tells them apart.
                b.lib.kssu_proxy_set(b.handle,1,0,0,100,False);b.step(0,60)
                transform(b,18)
                slots=set();kirby_swing=helper_in_swing=0
                for i in range(40):
                    state=b.state();b.world(floor,state['x_raw'],state['y_raw'])
                    b.lib.kssu_proxy_state(b.handle,1,C.byref(q))
                    if i==0 or not q.flags&12:b.lib.kssu_proxy_set(b.handle,1,int((state['x_raw']/65536+44)*65536),151*65536,32767,True)
                    b.step(2 if i<20 else 0)
                    for record in b.hits():
                        if record['slot']==14 or -struct.unpack('<h',struct.pack('<H',record['words'][2]))[0]<=0 or not b.owns(record['owner']):continue
                        if record['owner']==player or b.lib.kssu_helper_owns(b.handle,record['owner'])==0:
                            kirby_swing+=1;slots.add(record['slot'])
                assert kirby_swing>0,'Kirby produced no damaging record of his own'
                return {'helper_nodes':1,'first_hit_frame':hit,'helper_damaging_records':helper_attack,'kirby_damaging_records_while_idle':kirby_attack,'kirby_damaging_records_attacking':kirby_swing,'kirby_attack_slots':sorted(slots)}
            before=h.x_raw
            def probe_cells():return [struct.unpack('<hh',b.read(0x020bb784+0x14c+i*28,4)) for i in range(10)]
            was=probe_cells()
            assert b.lib.kssu_move_helper(b.handle,5*65536,65536)==0
            after=helper();assert after.x_raw==before+5*65536 and after.y_raw==h.y_raw+65536
            probes=probe_cells()
            assert all(q[0]==p[0]+5 and q[1]==p[1]+1 for p,q in zip(was,probes)),(was,probes)
            for i in range(30):
                state=b.state();b.world(floor,state['x_raw'],state['y_raw']);b.step(0)
            assert helper() is not None
            return {'moved_raw':[after.x_raw-before,after.y_raw-h.y_raw],'probes_moved_with_helper':True,'helper_alive_after':True}
        if name=='obj_vram_block':
            # The host reads engine A's OBJ VRAM whole on every frame, as one block.
            # A request that is not whole 8-byte words still takes one bus read a byte.
            b.lib.kssu_read_gpu.argtypes=[C.c_void_p,C.c_uint32,C.c_void_p,C.c_size_t]
            def gpu(address,length):
                out=C.create_string_buffer(length);assert b.lib.kssu_read_gpu(b.handle,address,out,length)==0;return out.raw
            transform(b,18);b.step(2,30)
            block=gpu(0x06400000,0x40000);slow=gpu(0x06400001,0x40000-2)
            assert block[1:-1]==slow, 'Block OBJ VRAM read differs from the bus read'
            assert gpu(0x06404040,4096)==block[0x4040:0x5040], 'Partial block read differs'
            used=sum(1 for v in block if v);assert used>10000,used
            return {'bytes':len(block),'nonzero_bytes':used,'equals_bus_read':True}
        if name=='main_screen_only':
            # The host shows neither DS screen and reads only engine A. With engine B's
            # drawing left out, main RAM, the GPU memory, the layer capture, the top
            # screen and the save state stay the same; the bottom screen is black.
            import hashlib
            b.lib.kssu_read_gpu.argtypes=[C.c_void_p,C.c_uint32,C.c_void_p,C.c_size_t]
            b.lib.kssu_read_layers.argtypes=[C.c_void_p,C.c_void_p,C.c_size_t,C.c_uint32]
            b.lib.kssu_main_screen_only.argtypes=[C.c_void_p,C.c_bool]
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)]
            seed=bytes(ROOT/'oracle/ready2/000.state');path=ROOT/'oracle/main-screen-test.state'
            masks=[0]*20+[16]*60+[17]*10+[16]*30+[2]*60+[32]*40+[1]*20
            def run(only):
                assert b.lib.kssu_load_state(b.handle,seed)==0 and b.lib.kssu_main_screen_only(b.handle,only)==0
                digest=hashlib.sha256();layer=(C.c_uint32*(256*192))();screen=C.create_string_buffer(256*192*4);bottom=set()
                for i in range(240):
                    state=b.state();b.world(floor,state['x_raw'],state['y_raw']);b.step(masks[i%len(masks)])
                    for address,length in ((0x04000000,0x70),(0x05000000,0x800),(0x07000000,0x800),(0x06400000,0x40000)):
                        out=C.create_string_buffer(length);assert b.lib.kssu_read_gpu(b.handle,address,out,length)==0;digest.update(out.raw)
                    if i%8==7:digest.update(b.read(0x02000000,0x400000))
                    if i>=4:
                        # The first frames after a load still show the run before it.
                        for plane in range(5):
                            assert b.lib.kssu_read_layers(b.handle,layer,256*192,plane)==0;digest.update(bytes(layer))
                        assert b.lib.kssu_framebuffer(b.handle,screen,len(screen),False)==0;digest.update(screen.raw)
                        assert b.lib.kssu_framebuffer(b.handle,screen,len(screen),True)==0;bottom.add(screen.raw)
                b.save(path);saved=hashlib.sha256(path.read_bytes()).hexdigest();path.unlink()
                return digest.hexdigest(),saved,bottom
            full=run(False);only=run(True);again=run(False)
            assert full[:2]==only[:2]==again[:2], (full[:2],only[:2],again[:2])
            black=b'\x00\x00\x00\xff'*(256*192)
            assert only[2]=={black} and black not in full[2] and full[2]==again[2], 'Bottom screen: black only while engine B is left out'
            return {'frames':240,'machine_and_engine_a_digest':full[0][:16],'save_state_sha256':full[1][:16],'same_with_engine_b_left_out':True,'bottom_screen_black_only_when_left_out':True}
        if name=='skip_idle_main':
            # #148: the host reads engine A's drawing only to find a screen-wide palette or
            # colour effect. With idle scanlines left undrawn, main RAM, the GPU memory and
            # the save state stay the same, and every frame gives the host the same answer:
            # the bridge's word for an undrawn frame, the same capture for a drawn one.
            import hashlib,collections
            l=b.lib;l.kssu_read_gpu.argtypes=[C.c_void_p,C.c_uint32,C.c_void_p,C.c_size_t]
            l.kssu_read_layers.argtypes=[C.c_void_p,C.c_void_p,C.c_size_t,C.c_uint32]
            l.kssu_skip_idle_main.argtypes=[C.c_void_p,C.c_char_p,C.c_size_t];l.kssu_main_frame.argtypes=[C.c_void_p]
            l.kssu_set_ability.argtypes=[C.c_void_p,C.c_uint8]
            def gpu(address,length):
                out=C.create_string_buffer(length);assert l.kssu_read_gpu(b.handle,address,out,length)==0;return out.raw
            palette=gpu(0x05000000,512);seed16=struct.unpack('<256H',palette);layer=(C.c_uint32*(256*192))()
            assert l.kssu_skip_idle_main(b.handle,palette,511)==-1 and l.kssu_main_frame(b.handle)==0
            def plane(index):
                assert l.kssu_read_layers(b.handle,layer,256*192,index)==0;return bytes(layer)
            def effect():
                # As NdsBackgroundEffectDecoder: one palette on every scanline, every pixel valid
                # with no colour effect control, then that palette against the seed's. Any other
                # palette is named by the planes the decoder reads.
                lines=plane(4);control=plane(2)
                if lines!=lines[:1024]*192 or min(control[3::4])<0x80 or any(control[1::4]) or any(control[2::4]):return 'unsupported'
                if all((a^b)&0x7fff==0 for a,b in zip(struct.unpack('<256I',lines[:1024]),seed16)):return 'identity'
                return hashlib.sha256(plane(0)+control+lines).hexdigest()[:16]
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)]
            seed=bytes(ROOT/'oracle/ready2/000.state');path=ROOT/'oracle/skip-idle-test.state'
            masks=[0]*20+[16]*60+[17]*10+[16]*30+[2]*60+[32]*40+[1]*20
            def run(skip):
                assert l.kssu_load_state(b.handle,seed)==0 and l.kssu_skip_idle_main(b.handle,palette if skip else None,512 if skip else 0)==0
                digest=hashlib.sha256();effects=[];drawn=[]
                for i in range(480):
                    state=b.state();b.world(floor,state['x_raw'],state['y_raw'])
                    # Ordinary play, then Crash: its flash recolours the whole background.
                    if i==250:assert l.kssu_set_ability(b.handle,23)==0
                    b.step(masks[i] if i<240 else 2 if 270<=i<274 else 0)
                    for address,length in ((0x04000000,0x70),(0x05000000,0x800),(0x07000000,0x800),(0x06400000,0x40000)):digest.update(gpu(address,length))
                    if i%8==7:digest.update(b.read(0x02000000,0x400000))
                    drawn.append(l.kssu_main_frame(b.handle))
                    effects.append({1:'identity',2:'unsupported'}.get(drawn[-1]) or effect())
                b.save(path);saved=hashlib.sha256(path.read_bytes()).hexdigest();path.unlink()
                return digest.hexdigest(),saved,effects,collections.Counter(drawn)
            full=run(False);skip=run(True);again=run(False)
            assert full[:3]==skip[:3]==again[:3], 'Leaving idle scanlines undrawn changed the machine or an effect'
            kinds=collections.Counter(e if e in ('identity','unsupported') else 'changed' for e in full[2])
            assert full[3]==again[3]=={0:480} and skip[3][0]==kinds['changed']>30 and skip[3][1]==kinds['identity']>400 and skip[3][2]==0,(skip[3],kinds)
            assert all(e=='identity' for e in full[2][:240]), 'Ordinary play must be idle'
            return {'frames':480,'machine_digest':full[0][:16],'save_state_sha256':full[1][:16],'same_with_idle_scanlines_undrawn':True,'undrawn_frames':skip[3][1],'drawn_effect_frames':skip[3][0],'partly_drawn_frames':skip[3][2],'unsupported_frames':kinds['unsupported']}
        if name=='arm9_clock':
            # #150: the host may run the ARM9 at twice the DS clock. Where the original
            # finishes its pass inside a DS frame nothing changes; in a crowd, where it
            # does not, every pass now ends in its frame. A state remembers its clock.
            import hashlib
            l=b.lib;l.kssu_arm9_clock.argtypes=[C.c_void_p,C.c_uint32];l.kssu_unfinished_passes.argtypes=[C.c_void_p]
            assert l.kssu_arm9_clock(b.handle,0)==-1 and l.kssu_arm9_clock(b.handle,3)==-1
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)]
            b.world(floor,384*65536,int(153.5*65536));b.step(0,60);transform(b,18)
            start=ROOT/'oracle/arm9-clock-test.state';middle=ROOT/'oracle/arm9-clock-mid-test.state';b.save(start)
            def play(clock,crowd,frames,state=start):
                assert l.kssu_arm9_clock(b.handle,clock)==0 and l.kssu_load_state(b.handle,bytes(state))==0
                passes=l.kssu_unfinished_passes(b.handle);trace=[]
                for i in range(frames):
                    at=b.state();b.world(floor,at['x_raw'],at['y_raw'])
                    for slot in range(1,crowd+1):
                        x=(at['x_raw']>>16)+(-120+slot*8 if slot%2 else 30+slot*6)
                        assert l.kssu_proxy_set(b.handle,slot,x*65536,151*65536,32760,True)==0
                        l.kssu_proxy_options(b.handle,slot,18,True);l.kssu_proxy_size(b.handle,slot,24,34)
                    at=b.step((2 if i%24<10 else 0)|(16 if (i//90)%2==0 else 32)|(1 if i%60==5 else 0))
                    trace.append((at['frame'],at['x_raw'],at['y_raw'],at['animation'],at['graphic'],at['flags']))
                return trace,l.kssu_unfinished_passes(b.handle)-passes
            alone=[play(clock,0,240) for clock in (1,2,1)]
            assert alone[0]==alone[1]==alone[2] and alone[0][1]==0, 'Without a crowd both clocks must give the same frames'
            slow,fast=play(1,31,300),play(2,31,300)
            assert slow[1]>0 and fast[1]==0,(slow[1],fast[1])
            # The crowd again at 2x, saved part of the way: the saved state continues as the run did.
            def digest():
                b.save(middle);data=middle.read_bytes()
                return hashlib.sha256(b.read(0x02000000,0x400000)).hexdigest(),hashlib.sha256(data).hexdigest(),data[-8:]==b'KSSUCK01'
            play(2,31,120);saved=digest();assert saved[2], 'A state saved at 2x names its clock'
            kept=ROOT/'oracle/arm9-clock-kept-test.state';kept.write_bytes(middle.read_bytes())
            b.step(2,60);straight=digest()
            assert l.kssu_load_state(b.handle,bytes(kept))==0;b.step(2,60);resumed=digest()
            assert straight==resumed, 'A state saved and loaded at 2x must continue as the run did'
            # The same state at the DS clock runs on, and a state saved there keeps the old format.
            assert l.kssu_arm9_clock(b.handle,1)==0 and l.kssu_load_state(b.handle,bytes(kept))==0
            frame=b.state()['frame'];b.step(2,60);assert b.state()['frame']==frame+60 and not digest()[2]
            for path in (start,middle,kept):path.unlink()
            return {'frames_without_crowd':240,'same_frames_at_both_clocks':True,'crowd':31,'crowd_frames':300,'unfinished_passes_ds_clock':slow[1],'unfinished_passes_doubled':fast[1],'state_saved_doubled_continues_exactly':True,'doubled_state_runs_at_ds_clock':True}
        if name=='recompiler':
            # #153: the original on the emulator's recompiler. The frames are the same ones,
            # an armed call still runs (on the interpreter), a state from either continues on
            # either, and code changed in RAM is compiled again.
            l=b.lib;l.kssu_recompiler.argtypes=[C.c_void_p,C.c_bool];l.kssu_code_changed.argtypes=[C.c_void_p]
            l.kssu_set_ability.argtypes=[C.c_void_p,C.c_uint8];l.kssu_ability_change_pending.argtypes=[C.c_void_p]
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)]
            b.world(floor,384*65536,int(153.5*65536));b.step(0,60);transform(b,18)
            start=ROOT/'oracle/recompiler-test.state';middle=ROOT/'oracle/recompiler-mid-test.state';b.save(start)
            def frame(i,crowd=8):
                at=b.state();b.world(floor,at['x_raw'],at['y_raw'])
                for slot in range(1,crowd+1):
                    x=(at['x_raw']>>16)+(-120+slot*8 if slot%2 else 30+slot*6)
                    assert l.kssu_proxy_set(b.handle,slot,x*65536,151*65536,32760,True)==0
                    l.kssu_proxy_options(b.handle,slot,18,True);l.kssu_proxy_size(b.handle,slot,24,34)
                at=b.step((2 if i%24<10 else 0)|(16 if (i//90)%2==0 else 32)|(1 if i%60==5 else 0))
                return (at['x_raw'],at['y_raw'],at['animation'],at['graphic'],at['flags'])
            def play(compiled,first,last,state=start):
                assert l.kssu_recompiler(b.handle,compiled)==0 and l.kssu_load_state(b.handle,bytes(state))==0
                return [frame(i) for i in range(first,last)]
            plain=play(False,0,300);fast=play(True,0,300)
            assert plain==fast==play(False,0,300), 'The recompiler must give the interpreter\'s frames'
            # A state saved on one continues on the other as the run did.
            for saved_on in (True,False):
                assert play(saved_on,0,120)==plain[:120];b.save(middle)
                for continued_on in (True,False):
                    assert play(continued_on,120,300,middle)==plain[120:], (saved_on,continued_on)
            # An armed call runs although its hook is in the interpreter loop.
            ability=lambda:b.read(0x020baf7b,1)[0]
            def crash(compiled):
                assert l.kssu_recompiler(b.handle,compiled)==0 and l.kssu_load_state(b.handle,bytes(start))==0
                b.step(0,60);assert l.kssu_set_ability(b.handle,23)==0
                for switch in range(1,21):
                    b.step()
                    if not l.kssu_ability_change_pending(b.handle):break
                assert ability()==23,ability()
                b.step(2,3)
                for revert in range(4,400):
                    b.step()
                    if ability()==1:return switch,revert
                raise AssertionError('Crash never reverted')
            calls=crash(False),crash(True)
            assert calls[0]==calls[1],calls
            # Code the host changes in RAM: the player update made to return at once.
            assert l.kssu_recompiler(b.handle,True)==0 and l.kssu_load_state(b.handle,bytes(start))==0
            def walk():
                x=b.state()['x_raw']
                for _ in range(30):
                    at=b.state();b.world(floor,at['x_raw'],at['y_raw']);b.step(16)
                return (b.state()['x_raw']-x)>>16
            entry=b.read(0x020b2714,4);moving=walk();assert moving>20,moving
            b.write(0x020b2714,struct.pack('<I',0xe12fff1e));stale=walk()
            assert l.kssu_code_changed(b.handle)==0;stopped=walk()
            b.write(0x020b2714,entry);assert l.kssu_code_changed(b.handle)==0;restored=walk()
            assert stopped==0 and restored>20,(stale,stopped,restored)
            # With #150: a block counts its cycles at the clock it was compiled at, so a clock
            # change drops the compiled blocks. A crowd at 2x, then at the DS clock from the
            # same frame on, plays as it does from a state saved there.
            import hashlib
            l.kssu_arm9_clock.argtypes=[C.c_void_p,C.c_uint32];l.kssu_unfinished_passes.argtypes=[C.c_void_p]
            after=ROOT/'oracle/recompiler-after-test.state'
            def crowd(frames):
                passes=l.kssu_unfinished_passes(b.handle);trace=[frame(i,31) for i in range(frames)];b.save(after)
                return trace,l.kssu_unfinished_passes(b.handle)-passes,hashlib.sha256(after.read_bytes()).hexdigest()
            assert l.kssu_arm9_clock(b.handle,2)==0 and l.kssu_load_state(b.handle,bytes(start))==0
            crowd(120);b.save(middle)
            assert l.kssu_arm9_clock(b.handle,1)==0;switched=crowd(180)
            assert l.kssu_load_state(b.handle,bytes(middle))==0;loaded=crowd(180)
            assert switched==loaded and loaded[1]>0,(switched[1:],loaded[1:],switched[0]==loaded[0])
            for path in (start,middle,after):path.unlink()
            return {'frames':300,'same_frames_as_interpreter':True,'state_from_either_continues_on_either':True,'crash_switch_and_revert':list(calls[1]),'walk_px':moving,'walk_px_unflushed_change':stale,'walk_px_after_flush':stopped,'walk_px_restored':restored,'clock_change_continues_as_saved_state':True,'crowd_unfinished_passes_after_clock_change':loaded[1]}
        if name=='hit_pool':
            # #151: an enemy holds one collision group for its body and one more while it
            # recoils from a hit. The original's pool held 48, so one attack into 24 or more
            # proxies emptied it and the original stopped for good. The bridge links two more
            # groups per proxy slot after the original's free ones. A Sword swing and a Crash,
            # each into 31 stacked proxies, now leave it running, and every proxy is hit. The
            # proxies are bosses so that the host position holds them stacked through recoil.
            l=b.lib;l.kssu_unfinished_passes.argtypes=[C.c_void_p]
            l.kssu_set_ability.argtypes=[C.c_void_p,C.c_uint8];l.kssu_ability_change_pending.argtypes=[C.c_void_p]
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)]
            groups=lambda:struct.unpack('<H',b.read(0x02076f8c,2))[0]
            def free():
                n=0;p=struct.unpack('<I',b.read(0x02076f84,4))[0]
                while p and n<500:n+=1;p=struct.unpack('<I',b.read(p,4))[0]
                return n
            assert groups()==48 and free()==47,(groups(),free())
            b.world(floor,384*65536,int(153.5*65536));assert groups()==112 and free()==111,(groups(),free())
            b.step(0,60);transform(b,18);q=Proxy()
            def place():
                at=b.state();b.world(floor,at['x_raw'],at['y_raw'])
                for slot in range(1,32):
                    assert l.kssu_proxy_set(b.handle,slot,((at['x_raw']>>16)+55+(slot-1)%8)*65536,151*65536,32760,True)==0
                    l.kssu_proxy_options(b.handle,slot,18,True);l.kssu_proxy_size(b.handle,slot,24,34)
            def attack(held):
                passes=l.kssu_unfinished_passes(b.handle);hit=set();low=free()
                for i in range(300):
                    place();b.step(2 if i<held else 0)
                    if i<60:
                        low=min(low,free())
                        for slot in range(1,32):
                            assert l.kssu_proxy_state(b.handle,slot,C.byref(q))==0
                            if q.hp<32760 or q.flags&2:hit.add(slot)
                x=b.state()['x_raw']
                for _ in range(60):place();b.step(16)
                return {'proxies_hit':len(hit),'fewest_free_groups':low,'frames_ended_inside_a_pass':l.kssu_unfinished_passes(b.handle)-passes,'walk_px_after':(b.state()['x_raw']-x)>>16}
            for _ in range(60):place();b.step(0)
            sword=attack(10)
            # Crash through the F slot's call, as in case `recompiler`.
            assert l.kssu_set_ability(b.handle,23)==0
            for _ in range(20):
                place();b.step()
                if not l.kssu_ability_change_pending(b.handle):break
            assert b.state()['ability']==23,b.state()['ability']
            crash=attack(3)
            for result in (sword,crash):
                assert result['proxies_hit']==31 and result['fewest_free_groups']>0,result
                assert result['frames_ended_inside_a_pass']<20 and result['walk_px_after']>40,result
            return {'groups':groups(),'proxies':31,'sword':sword,'crash':crash}
        if name=='step_between_passes':
            # #134: a source frame ends as VBlank begins, between two logic passes of the
            # original. Replayed as the runtime places Kirby (the terrain window rebased
            # every frame, host movement added), his body OBJ in the hardware OAM stays
            # where the state of the frame before puts it. A frame that ended 71 scanlines
            # later was inside the pass whenever a helper made it longer, and a window
            # moved under it drew Kirby a terrain cell off for one frame.
            import math
            l=b.lib;l.kssu_read_gpu.argtypes=[C.c_void_p,C.c_uint32,C.c_void_p,C.c_size_t]
            l.kssu_move_player.argtypes=[C.c_void_p,C.c_int32,C.c_int32];l.kssu_unfinished_passes.argtypes=[C.c_void_p]
            oam=C.create_string_buffer(1024)
            def body():
                assert l.kssu_read_gpu(b.handle,0x07000000,oam,1024)==0;xs=[];ys=[]
                for k in range(128):
                    a0,a1,a2=struct.unpack_from('<3H',oam.raw,k*8)
                    if (a0==0xf8 and a1==0) or (a0&0x300)==0x200 or a2>>12:continue
                    x=a1&511;y=a0&255;xs.append(x-512 if x>256 else x);ys.append(y-256 if y>192 else y)
                return (min(xs),min(ys)) if xs else None
            origin=lambda s:(math.floor(s['x_raw']/65536)-s['camera_x'],math.floor(s['y_raw']/65536)-s['camera_y'])
            def replay(masks,feeds={},nudge=False):
                hx=20*24+12.0;hy=30*24-14.5;window=None;stepped=origin(b.state());offsets=[];moves=held=bias=0;late=False
                count=l.kssu_unfinished_passes(b.handle)
                for n,mask in enumerate(masks):
                    if n in feeds:assert b.feed(feeds[n])==0
                    # The host moves the window a cell just when the original is inside a long pass.
                    if nudge and late:bias^=1
                    ox=math.floor(hx/24)-16+bias;oy=math.floor(hy/24)-4
                    # KirbyNdsProvider.HoldWindow: neither the window nor Kirby moves until that pass ends.
                    if late and window:ox,oy=window;held+=1
                    cells=[3 if row+oy>=30 else 0 for row in range(9) for column in range(100)]
                    tx=round((hx-ox*24)*65536);ty=round((hy-oy*24)*65536);state=b.state()
                    if window:
                        fx=state['x_raw']+(window[0]-ox)*24*65536;fy=state['y_raw']+(window[1]-oy)*24*65536;b.world(cells,fx,fy)
                        if (abs(tx-fx)>1 or abs(ty-fy)>1) and not late:assert l.kssu_move_player(b.handle,tx-fx,ty-fy)==0
                    else:b.world(cells,tx,ty)
                    moves+=window not in (None,(ox,oy));window=(ox,oy)
                    state=b.step(mask);shown=stepped;stepped=origin(state);at=body()
                    offsets.append(at and (at[0]-shown[0],at[1]-shown[1]))
                    now=l.kssu_unfinished_passes(b.handle);late=now!=count;count=now
                    hx+=(state['x_raw']/65536+ox*24-hx)*3.71;hy+=(state['y_raw']/65536+oy*24-hy)*1.91
                far=lambda a,c:max(abs(a[0]-c[0]),abs(a[1]-c[1]))>=12
                # Off for one frame and back: a pose change stays where it went.
                jumps=[(i,offsets[i-1],offsets[i]) for i in range(1,len(offsets)-1) if all(offsets[i-1:i+2]) and far(offsets[i],offsets[i-1]) and not far(offsets[i+1],offsets[i-1])]
                return jumps,moves,held
            # The first frames from the seed state, which was saved at the end of a frame.
            assert l.kssu_unfinished_passes(b.handle)==0
            jumps,moves,_=replay([0]*20+[16]*60);assert not jumps and moves and l.kssu_unfinished_passes(b.handle)==0,(jumps,moves)
            b.step(0,30);transform(b,18);b.step(1024,1);b.step(0,150)
            assert b.state()['ability']==1 and l.kssu_unfinished_passes(b.handle)>=3, 'The original takes longer than a frame to load an ability or a helper'
            # Kirby copies Sword again beside the helper while the window moves under the load.
            before=l.kssu_unfinished_passes(b.handle)
            jumps,nudged,held=replay([18]*20+[2,0,128]+[16]*120,{20:18},nudge=True)
            loading=l.kssu_unfinished_passes(b.handle)
            assert b.state()['ability']==18 and loading>before and held==loading-before and not jumps,(jumps,held,before,loading)
            flap=lambda direction,frames:[(1 if i%16<8 else 0)|direction for i in range(frames)]
            masks=[0]*40+[1]*6+[0]*6+flap(16,180)+flap(32,180)+[16]*60+[32]*60+[2]*30+flap(16,60)
            jumps,moves,_=replay(masks)
            assert not jumps and moves>=60,(jumps,moves)
            assert l.kssu_unfinished_passes(b.handle)==loading, 'A frame ended inside a logic pass with only Kirby and his helper'
            # A state saved where a frame now ends continues from there.
            path=ROOT/'oracle/step-test.state';b.save(path)
            def run():
                trace=[]
                for mask in [16]*20+[1]*10:
                    state=b.state();b.world([3 if y>=7 else 0 for y in range(9) for x in range(100)],state['x_raw'],state['y_raw']);state=b.step(mask);trace.append((state['x_raw'],state['y_raw'],body()))
                return trace
            first=run();assert l.kssu_load_state(b.handle,bytes(path))==0;path.unlink();assert run()==first, 'The saved state did not continue the same'
            assert l.kssu_unfinished_passes(b.handle)==loading
            return {'frames':80+143+len(masks),'window_moves':moves+nudged,'frames_ended_inside_a_pass':{'with_kirby_and_helper':0,'while_original_loads':loading},'frames_held_for_a_load':held,'one_frame_body_excursions_of_12px':0,'state_saved_at_vblank_continues_the_same':True}
        raise ValueError(name)
    finally:b.close()


def survey(cases,clock):
    """Every case at another ARM9 clock, against the evidence of the DS clock. The cases expect the DS clock: a failure here is a difference to explain, not a defect."""
    path=ROOT/'oracle/bridge-test-results.json'
    recorded={item['case']:item['result'] for item in json.loads(path.read_text())} if path.is_file() else {}
    same=[];different=[];failed=[]
    for name in cases:
        process=subprocess.run([sys.executable,__file__,'--case',name,'--arm9-clock',str(clock)],capture_output=True,text=True,timeout=60)
        lines=[line[7:] for line in process.stdout.splitlines() if line.startswith('RESULT ')]
        if process.returncode or not lines:
            reason=[line for line in process.stderr.splitlines() if line.strip()][-1:] or ['no result']
            failed.append({'case':name,'reason':reason[0][:300]});print(f'FAIL {name}: {reason[0][:300]}',flush=True);continue
        result=json.loads(lines[-1])['result']
        if name in recorded and result==recorded[name]:same.append(name);print(f'SAME {name}',flush=True)
        else:different.append({'case':name,'result':result,'ds_clock':recorded.get(name)});print(f'DIFF {name}: '+json.dumps(result),flush=True)
    out=ROOT/f'oracle/bridge-test-survey-arm9x{clock}.json'
    out.write_text(json.dumps({'clock':clock,'same':same,'different':different,'failed':failed},indent=2)+'\n')
    print(f'{len(cases)} cases at {clock}x the DS clock: {len(same)} same, {len(different)} different, {len(failed)} failed. Survey: {out}')

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--case');parser.add_argument('--skip-abilities',action='store_true')
    parser.add_argument('--arm9-clock',type=int,choices=[1,2],default=1,help='2 surveys every case at twice the DS clock (#150): it lists the cases that fail or answer differently and replaces no evidence.')
    args=parser.parse_args()
    if args.case:
        result=run_case(args.case,args.arm9_clock)
        # The emulator's log shares stdout and may stop mid-line: start the result on its own line.
        print('\nRESULT '+json.dumps({'case':args.case,'result':result}),flush=True);return
    cases=['scene_doors', 'far_shot_draw', 'room_food', 'proxy_birth_contact', 'proxy_birth_inhale', 'proxy_contact', 'proxy_contact_sword', 'proxy_touch_off', 'proxy_swallow_touch_off', 'proxy_suplex_touch_off', 'proxy_ice_detach', 'proxy_ice_release', 'proxy_lethal', 'motion', 'ladder', 'ladder_top', 'ladder_top_stomp', 'one_way', 'solid_ceiling', 'water', 'proxy_water', 'proxy_water_sword', 'proxy_water_below', 'hurt_water', 'proxy_swallow', 'hazard_spit', 'hazard_swallow', 'star_spit', 'star_spit_wall', 'star_big', 'star_ability', 'proxy_sword', 'proxy_reuse', 'proxy_save', 'proxy_mix', 'mix_no_limited', 'boss_inhale', 'boss_suplex', 'boss_cook', 'boss_sword', 'proxy_capacity', 'copy_drop_reabsorb', 'wheelie_ride', 'proxy_suplex', 'proxy_cook', 'audio', 'hurt', 'hurt_lethal', 'hurt_cold', 'guard', 'health_invulnerable', 'health_sync', 'proxy_size', 'f_slot', 'f_slot_save', 'f_slot_plasma', 'move_mouthful', 'helper_reaction', 'helper_move', 'helper_owns', 'obj_vram_block', 'main_screen_only', 'skip_idle_main', 'arm9_clock', 'recompiler', 'hit_pool', 'step_between_passes']
    if not args.skip_abilities:cases += [f'ability-{i}' for i in range(2,26)]
    if args.arm9_clock!=1:return survey(cases,args.arm9_clock)
    results=[]
    for name in cases:
        process=subprocess.run([sys.executable,__file__,'--case',name],capture_output=True,text=True,timeout=30)
        lines=[line[7:] for line in process.stdout.splitlines() if line.startswith('RESULT ')]
        if process.returncode or not lines:
            raise SystemExit(f'{name} failed (exit {process.returncode}):\n{process.stderr[-2500:]}')
        item=json.loads(lines[-1]);results.append(item);print(f'PASS {name}: '+json.dumps(item['result']),flush=True)
    path=ROOT/'oracle/bridge-test-results.json';path.write_text(json.dumps(results,indent=2)+'\n')
    print(f'{len(results)} original-ROM cases passed. Evidence: {path}')
if __name__=='__main__':main()
