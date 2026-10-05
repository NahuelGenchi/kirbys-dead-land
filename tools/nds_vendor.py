"""Checked, idempotent additions to the pinned melon-rs wrapper (GPU reads and bounded savestate buffers)."""
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
def check_gpu_patch(vendor: Path, revision: str, apply: bool = False):
    for relative, (before, after) in GPU_EDITS.items():
        original = subprocess.check_output(['git','-C',str(vendor),'show',f'{revision}:{relative}'],text=True)
        if original.count(before) != 1:
            raise SystemExit(f'GPU patch precondition failed: {relative}')
        patched = original.replace(before, after)
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
        if target.read_text() not in (original,legacy,previous,patched):
            raise SystemExit(f'Unexpected GPU wrapper edits: {relative}; left untouched.')
        if apply and target.read_text()!=patched:
            target.write_text(patched)
