//! Five melonDS 82fdbc78 `Platform` callbacks that the pinned melon-rs shim
//! (51f25eda) does not define, for the Windows build only (#242). Only melonDS's
//! GBA add-on code calls them, and nothing reaches that code: the Linux linker
//! drops it together with its references, while MinGW's linker reports the
//! references before it drops the code. Each one does nothing. The bridge
//! (`src/nds/lib.rs`) and the oracle runner (`tools/oracle_runner.rs`) both use
//! this file; the names are the callbacks' Itanium C++ names.
use std::ffi::c_void;

#[export_name = "_ZN7melonDS8Platform12WriteGBASaveEPKhjjjPv"]
extern "C" fn write_gba_save(_data: *const u8, _length: u32, _offset: u32, _count: u32, _user: *mut c_void) {}

#[export_name = "_ZN7melonDS8Platform13Addon_KeyDownENS0_7KeyTypeEPv"]
extern "C" fn addon_key_down(_key: i32, _user: *mut c_void) -> bool {
    false
}

#[export_name = "_ZN7melonDS8Platform17Addon_RumbleStartEjPv"]
extern "C" fn addon_rumble_start(_milliseconds: u32, _user: *mut c_void) {}

#[export_name = "_ZN7melonDS8Platform16Addon_RumbleStopEPv"]
extern "C" fn addon_rumble_stop(_user: *mut c_void) {}

#[export_name = "_ZN7melonDS8Platform17Addon_MotionQueryENS0_15MotionQueryTypeEPv"]
extern "C" fn addon_motion_query(_kind: i32, _user: *mut c_void) -> f32 {
    0.0
}
