#!/usr/bin/env python3
"""ctypes reference client for the private original-ROM execution bridge."""
import ctypes as C
import os
from pathlib import Path
ROOT=Path.home()/'.local/share/kirby-dead-cells'
ROM=Path(os.environ.get('KSSU_ROM',Path.home()/'Documents/ROMS/Nintendo DS/Kirby Super Star Ultra (USA).nds'))
class State(C.Structure):
    _fields_=[('frame',C.c_uint32),('player',C.c_uint32),('x_raw',C.c_int32),('y_raw',C.c_int32),('vx_raw',C.c_int16),('vy_raw',C.c_int16),('camera_x',C.c_int16),('camera_y',C.c_int16),('ability',C.c_uint8),('kind',C.c_uint8),('flags',C.c_uint8),('facing_raw',C.c_uint8),('graphic',C.c_uint32),('animation',C.c_uint32),('state_words',C.c_uint32*16)]
    def dict(self):return {name:list(getattr(self,name)) if name=='state_words' else getattr(self,name) for name,_ in self._fields_}
class Hit(C.Structure):
    _fields_=[('address',C.c_uint32),('owner',C.c_uint32),('slot',C.c_uint32),('bounds',C.c_int16*4),('definition',C.c_uint32),('words',C.c_uint16*14)]
class Bridge:
    def __init__(self,state=ROOT/'oracle/ready2/000.state'):
        self.lib=C.CDLL(str(ROOT/'oracle/vendor/melon-rs/target/release/libkssu_bridge.so'))
        l=self.lib;l.kssu_open.argtypes=[C.c_char_p,C.c_char_p];l.kssu_open.restype=C.c_void_p
        l.kssu_close.argtypes=[C.c_void_p];l.kssu_step.argtypes=[C.c_void_p,C.c_uint16,C.c_uint32]
        l.kssu_state.argtypes=[C.c_void_p,C.POINTER(State)]
        l.kssu_hits.argtypes=[C.c_void_p,C.POINTER(Hit),C.c_size_t]
        for name in ['kssu_read','kssu_write']:getattr(l,name).argtypes=[C.c_void_p,C.c_uint32,C.c_void_p,C.c_size_t]
        l.kssu_framebuffer.argtypes=[C.c_void_p,C.c_void_p,C.c_size_t,C.c_bool]
        l.kssu_save_state.argtypes=[C.c_void_p,C.c_char_p]
        l.kssu_world.argtypes=[C.c_void_p,C.POINTER(C.c_uint16),C.c_size_t,C.c_int32,C.c_int32]
        l.kssu_action_flags.argtypes=[C.c_void_p]
        l.kssu_feed.argtypes=[C.c_void_p,C.c_uint8]
        l.kssu_player_owns.argtypes=[C.c_void_p,C.c_uint32]
        self.handle=l.kssu_open(bytes(ROM),bytes(state));assert self.handle
    def close(self):
        if self.handle:self.lib.kssu_close(self.handle);self.handle=None
    def read(self,addr,length):
        out=C.create_string_buffer(length);assert self.lib.kssu_read(self.handle,addr,out,length)==0;return out.raw
    def write(self,addr,data):
        assert self.lib.kssu_write(self.handle,addr,data,len(data))==0
    def state(self):
        s=State();assert self.lib.kssu_state(self.handle,C.byref(s))==0;return s.dict()
    def step(self,mask=0,frames=1):
        assert self.lib.kssu_step(self.handle,mask,frames)==0;return self.state()
    def hits(self):
        out=(Hit*512)();count=self.lib.kssu_hits(self.handle,out,512);assert count>=0;return [{n:list(getattr(h,n)) if n in ('bounds','words') else getattr(h,n) for n,_ in Hit._fields_} for h in out[:count]]
    def screen(self,path,bottom=False):
        from PIL import Image
        out=C.create_string_buffer(256*192*4);assert self.lib.kssu_framebuffer(self.handle,out,len(out),bottom)==0
        Image.frombytes('RGBA',(256,192),out.raw,'raw','BGRA').save(path)
    def save(self,path):assert self.lib.kssu_save_state(self.handle,bytes(path))==0
    def world(self,cells,x_raw,y_raw):
        assert len(cells)==900
        data=(C.c_uint16*900)(*cells)
        assert self.lib.kssu_world(self.handle,data,900,x_raw,y_raw)==0
    def action_flags(self):return self.lib.kssu_action_flags(self.handle)
    def feed(self,ability):return self.lib.kssu_feed(self.handle,ability)
    def owns(self,address):return self.lib.kssu_player_owns(self.handle,address)==1
class Proxy(C.Structure):
    _fields_=[('entity',C.c_uint32),('flags',C.c_uint32),('x_raw',C.c_int32),('y_raw',C.c_int32),('hp',C.c_int32),('update',C.c_uint32)]
def bind_proxy_api(b):
    b.lib.kssu_proxy_set.argtypes=[C.c_void_p,C.c_uint32,C.c_int32,C.c_int32,C.c_int32,C.c_bool]
    b.lib.kssu_proxy_state.argtypes=[C.c_void_p,C.c_uint32,C.POINTER(Proxy)]
    b.lib.kssu_set_swallowed_ability.argtypes=[C.c_void_p,C.c_uint8]
    b.lib.kssu_load_state.argtypes=[C.c_void_p,C.c_char_p]
    b.lib.kssu_proxy_options.argtypes=[C.c_void_p,C.c_uint32,C.c_uint8,C.c_bool]
    b.lib.kssu_proxy_size.argtypes=[C.c_void_p,C.c_uint32,C.c_uint16,C.c_uint16]
    b.lib.kssu_proxy_contact.argtypes=[C.c_void_p,C.c_uint32,C.c_bool]
    b.lib.kssu_proxy_detach.argtypes=[C.c_void_p,C.c_uint32]
    b.lib.kssu_room_cleanup.argtypes=[C.c_void_p]
    b.lib.kssu_hurt.argtypes=[C.c_void_p,C.c_uint16,C.c_int32]
    b.lib.kssu_health.argtypes=[C.c_void_p]
    b.lib.kssu_max_health.argtypes=[C.c_void_p]
    b.lib.kssu_set_health.argtypes=[C.c_void_p,C.c_uint16,C.c_uint16]
