"""Checked, idempotent additions to the pinned melon-rs wrapper (GPU reads, bounded savestate buffers, the block OBJ VRAM copy, the main-screen-only switch and the idle main screen switch)."""
import subprocess
from pathlib import Path
GPU_EDITS = {
    'src/melon/nds.rs': (
        '    pub fn main_ram(&self) -> &[u8] {',
        '    pub fn bus_read8(&mut self, address: u32) -> u8 { self.0.pin_mut().ARM9Read8(address) }\n\n    pub fn read_savestate_bytes(&mut self, contents: &mut [u8]) -> bool {\n        if contents.len() < 16 || contents.len() > i32::MAX as usize { return false; }\n        unsafe { sys::ReadSavestate(self.0.pin_mut(), contents.as_mut_ptr(), contents.len() as i32) }\n    }\n\n    pub fn layer_capture(&mut self, plane: u32, dest: &mut [u32]) -> bool {\n        if plane > 4 || dest.len() < 256*192 { return false; }\n        unsafe { sys::Copy_LayerCapture(self.0.pin_mut(), dest.as_mut_ptr(), plane, dest.len() as u32) }\n    }\n\n    pub fn main_ram(&self) -> &[u8] {'),
    'src/melon/sys.rs': (
        '        pub unsafe fn MainRAM(nds: &NDS) -> *const u8;',
        '        pub fn ARM9Read8(self: Pin<&mut NDS>, addr: u32) -> u8;\n        pub unsafe fn Copy_LayerCapture(nds: Pin<&mut NDS>, dest: *mut u32, plane: u32, count: u32) -> bool;\n        pub unsafe fn MainRAM(nds: &NDS) -> *const u8;'),
}
# Second stage (#123), applied on top of the edits above: the block OBJ VRAM copy and the
# main-screen-only switch of tools/nds_graphics_patch.py.
GPU_EDITS_2 = {
    'src/melon/nds.rs': (
        '    pub fn main_ram(&self) -> &[u8] {',
        '    pub fn obj_vram(&mut self, offset: u32, dest: &mut [u8]) -> bool {\n        if dest.len() > 0x40000 { return false; }\n        unsafe { sys::Copy_ObjVram(self.0.pin_mut(), dest.as_mut_ptr(), offset, dest.len() as u32) }\n    }\n\n    pub fn set_main_screen_only(&mut self, enabled: bool) -> bool { sys::Set_MainScreenOnly(self.0.pin_mut(), enabled) }\n\n    pub fn main_ram(&self) -> &[u8] {'),
    'src/melon/sys.rs': (
        '        pub unsafe fn MainRAM(nds: &NDS) -> *const u8;',
        '        pub unsafe fn Copy_ObjVram(nds: Pin<&mut NDS>, dest: *mut u8, offset: u32, count: u32) -> bool;\n        pub fn Set_MainScreenOnly(nds: Pin<&mut NDS>, enabled: bool) -> bool;\n        pub unsafe fn MainRAM(nds: &NDS) -> *const u8;'),
}
# Third stage (#148): the idle main screen switch of tools/nds_graphics_patch.py.
GPU_EDITS_3 = {
    'src/melon/nds.rs': (
        '    pub fn main_ram(&self) -> &[u8] {',
        '    pub fn set_skip_idle_main(&mut self, palette: Option<&[u8; 512]>) -> bool {\n        unsafe { sys::Set_SkipIdleMain(self.0.pin_mut(), palette.map_or(std::ptr::null(), |p| p.as_ptr())) }\n    }\n\n    pub fn main_frame(&mut self) -> u32 { sys::Get_MainFrame(self.0.pin_mut()) }\n\n    pub fn main_ram(&self) -> &[u8] {'),
    'src/melon/sys.rs': (
        '        pub unsafe fn MainRAM(nds: &NDS) -> *const u8;',
        '        pub unsafe fn Set_SkipIdleMain(nds: Pin<&mut NDS>, palette: *const u8) -> bool;\n        pub fn Get_MainFrame(nds: Pin<&mut NDS>) -> u32;\n        pub unsafe fn MainRAM(nds: &NDS) -> *const u8;'),
}
def check_gpu_patch(vendor: Path, revision: str, apply: bool = False):
    for relative, (before, after) in GPU_EDITS.items():
        original = subprocess.check_output(['git','-C',str(vendor),'show',f'{revision}:{relative}'],text=True)
        if original.count(before) != 1:
            raise SystemExit(f'GPU patch precondition failed: {relative}')
        first = patched = original.replace(before, after)
        target = vendor/relative
        legacy = original.replace(before, after.split("    pub fn read_savestate_bytes")[0]+before) if "read_savestate_bytes" in after else patched
        previous = patched
        if relative.endswith('nds.rs'):
            start = previous.index('    pub fn layer_capture(')
            end = previous.index('    pub fn main_ram(', start)
            previous = previous[:start] + previous[end:]
            legacy = original.replace(before, '    pub fn bus_read8(&mut self, address: u32) -> u8 { self.0.pin_mut().ARM9Read8(address) }\n\n' + before)
        else:
            previous = previous.replace('        pub unsafe fn Copy_LayerCapture(nds: Pin<&mut NDS>, dest: *mut u32, plane: u32, count: u32) -> bool;\n', '')
        before, after = GPU_EDITS_2[relative]
        if first.count(before) != 1:
            raise SystemExit(f'GPU patch second-stage precondition failed: {relative}')
        second = first.replace(before, after)
        before, after = GPU_EDITS_3[relative]
        if second.count(before) != 1:
            raise SystemExit(f'GPU patch third-stage precondition failed: {relative}')
        patched = second.replace(before, after)
        if target.read_text() not in (original,legacy,previous,first,second,patched):
            raise SystemExit(f'Unexpected GPU wrapper edits: {relative}; left untouched.')
        if apply and target.read_text()!=patched:
            target.write_text(patched)
