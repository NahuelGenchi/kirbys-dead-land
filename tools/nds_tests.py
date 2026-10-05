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


def run_case(name):
    b=Bridge();bind_proxy_api(b)
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
        if name in ('proxy_suplex','proxy_cook'):
            ability=6 if name=='proxy_suplex' else 24;transform(b,ability)
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)];q=Proxy();damage=[];captured=0;gone=None
            for i in range(380):
                s=b.state();b.world(floor,s['x_raw'],s['y_raw'])
                result=b.lib.kssu_proxy_set(b.handle,1,75*65536,151*65536,32760,True)
                if result==-3:break
                assert result==0
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
        if name=='proxy_swallow':
            floor=[3 if y>=7 else 0 for y in range(9) for x in range(100)]
            b.world(floor,43*65536,int(153.5*65536));b.step(0,60);controlled=0;q=Proxy()
            for i in range(60):
                s=b.state();b.world(floor,s['x_raw'],s['y_raw'])
                assert b.lib.kssu_proxy_set(b.handle,1,85*65536,151*65536,100,True)==0
                b.step(2 if i>=5 else 0);b.lib.kssu_proxy_state(b.handle,1,C.byref(q))
                controlled+=bool(q.flags&32)
                if q.flags&4:break
            assert q.flags&4 and controlled>=15 and b.action_flags()&2
            assert b.lib.kssu_set_swallowed_ability(b.handle,18)==0
            b.step(0,1);b.step(128,1);b.step(0,180);assert b.state()['ability']==18
            assert b.lib.kssu_load_state(b.handle,bytes(ROOT/'oracle/ready2/000.state'))==0
            assert b.state()['ability']==1
            return {'original_inhale_frames':controlled,'source_swallow_evolution':18,'in_place_reload':True}
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
        raise ValueError(name)
    finally:b.close()


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--case');parser.add_argument('--skip-abilities',action='store_true');args=parser.parse_args()
    if args.case:
        print('RESULT '+json.dumps({'case':args.case,'result':run_case(args.case)}),flush=True);return
    cases=['scene_doors','room_food','proxy_birth_contact','proxy_birth_inhale','proxy_contact','proxy_contact_sword','proxy_ice_detach','proxy_lethal','motion','ladder','one_way','solid_ceiling','water','proxy_swallow','proxy_sword','proxy_reuse','proxy_save','proxy_mix','boss_inhale','boss_suplex','boss_cook','boss_sword','proxy_capacity','copy_drop_reabsorb','wheelie_ride','proxy_suplex','proxy_cook','audio','hurt','hurt_lethal','hurt_cold','guard','health_invulnerable','health_sync','proxy_size']
    if not args.skip_abilities:cases += [f'ability-{i}' for i in range(2,26)]
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
