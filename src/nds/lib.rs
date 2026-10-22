//! Original-ROM execution bridge. All gameplay runs on the emulated DS CPUs.
//! This library never opens a window/audio device or accesses a user's DS saves.
use chrono::{TimeZone, Utc};
use melon_rs::{input::ButtonMask, melon::nds::Nds};
use sha2::{Digest, Sha256};
use std::{
    collections::{HashMap, VecDeque},
    ffi::{c_char, CStr},
    fs, ptr,
    sync::{
        atomic::{AtomicU64, Ordering},
        OnceLock,
    },
};
fn native_trace(message: &str) {
    static ENABLED: OnceLock<bool> = OnceLock::new();
    if *ENABLED.get_or_init(|| {
        std::env::var_os("KSSU_NATIVE_TRACE").as_deref() == Some(std::ffi::OsStr::new("1"))
    }) {
        eprintln!("[kssu-native] {}", message);
    }
}
fn native_trace_once(bit: u32, message: &str) {
    static SEEN: AtomicU64 = AtomicU64::new(0);
    if SEEN.fetch_or(1 << bit, Ordering::Relaxed) & (1 << bit) == 0 {
        native_trace(message);
    }
}
const BASE: u32 = 0x02000000;
const SIZE: usize = 0x400000;
const PROXY_COUNT: usize = 32;
#[derive(Default, Copy, Clone)]
struct Proxy {
    entity: u32,
    normal_update: u32,
    flags: u32,
    hp: i32,
    x: i32,
    y: i32,
    born_frame: u32,
    width: u16,
    height: u16,
    options: u32,
    original_definition: u32,
}
struct Bridge {
    nds: Nds,
    proxies: [Proxy; PROXY_COUNT],
    audio: VecDeque<i16>,
    /// Every actor's anchor points as the last source frame began.
    anchors: HashMap<u32, [(i32, i32); 2]>,
    /// Off-screen OBJ pieces built with the last frame's shadow OAM, kept
    /// until the hardware shows that OAM (see `far_objects`).
    far_pending: Option<(Vec<FarObj>, Vec<u8>)>,
    /// Off-screen OBJ pieces of the OAM the hardware shows now.
    far_shown: Vec<FarObj>,
    /// Source frames that ended inside an original logic pass (see `kssu_unfinished_passes`).
    unfinished_passes: u32,
    /// The host asked for the main screen only (see `kssu_main_screen_only`).
    main_screen_only: bool,
    /// The palette of an idle scanline, while the host asked for those to be
    /// left undrawn (see `kssu_skip_idle_main`).
    idle_palette: Option<[u8; 512]>,
    /// `proxies` already describes main RAM as it is now. Every call that can
    /// change main RAM clears it, so reads between two such calls share one
    /// refresh. The size, options and contact setters change nothing the
    /// refresh reads and leave it (#160).
    proxies_fresh: bool,
    /// Each proxy slot's original object, from the last walk of the actor
    /// lists (#160). Only a call that runs or writes the machine can change
    /// it, and each clears it (`stale`), except `kssu_proxy_set`: its writes
    /// (positions, health, a spawn record's bytes, a retiring object's update)
    /// leave the lists and each object's kind, parent and record pointer as
    /// they are. A step's pairing then walks the lists once, not twice a proxy.
    proxy_tree: Option<[Option<usize>; PROXY_COUNT]>,
}
/// One OBJ piece the original OAM builder culled: full screen position, its
/// original attributes (x and y bits as the builder would write them) and the
/// number of on-screen OAM entries written before it.
#[repr(C)]
#[derive(Copy, Clone, Default)]
pub struct FarObj {
    x: i16,
    y: i16,
    attr0: u16,
    attr1: u16,
    attr2: u16,
    order: u16,
}
fn u16at(r: &[u8], p: usize) -> u16 {
    u16::from_le_bytes(r[p..p + 2].try_into().unwrap())
}
fn u32at(r: &[u8], p: usize) -> u32 {
    u32::from_le_bytes(r[p..p + 4].try_into().unwrap())
}
fn off(addr: u32, size: usize) -> Option<usize> {
    let p = addr.checked_sub(BASE)? as usize;
    (p.checked_add(size)? <= SIZE).then_some(p)
}
#[repr(C)]
#[derive(Default, Copy, Clone)]
pub struct KssuState {
    pub frame: u32,
    pub player: u32,
    pub x_raw: i32,
    pub y_raw: i32,
    pub vx_raw: i16,
    pub vy_raw: i16,
    pub camera_x: i16,
    pub camera_y: i16,
    pub ability: u8,
    pub kind: u8,
    pub flags: u8,
    pub facing_raw: u8,
    pub graphic: u32,
    pub animation: u32,
    pub state_words: [u32; 16],
}
#[repr(C)]
#[derive(Default, Copy, Clone)]
pub struct KssuHit {
    pub address: u32,
    pub owner: u32,
    pub slot: u32,
    pub bounds: [i16; 4],
    pub definition: u32,
    pub words: [u16; 14],
}
#[no_mangle]
pub unsafe extern "C" fn kssu_open(
    rom_path: *const c_char,
    state_path: *const c_char,
) -> *mut std::ffi::c_void {
    native_trace("open: entered");
    let run = std::panic::catch_unwind(|| {
        if rom_path.is_null() || state_path.is_null() {
            return None;
        }
        let rom = fs::read(CStr::from_ptr(rom_path).to_str().ok()?).ok()?;
        if rom.len() != 134217728 || rom.get(12..16) != Some(b"YKWE") {
            return None;
        }
        if format!("{:x}", Sha256::digest(&rom))
            != "b2573313a582c8dfaf2d93834a670c1ca8f389e5daa744e2dbe1a4ca903f3027"
        {
            return None;
        }
        native_trace("open: ROM validated");
        let state = CStr::from_ptr(state_path).to_str().ok()?.to_owned();
        let mut saved = fs::read(&state).ok()?;
        let clock = take_clock_trailer(&mut saved);
        let hook = take_hook_trailer(&mut saved);
        let metadata = read_metadata(&saved);
        if metadata.is_some() {
            saved.truncate(saved.len() - metadata_len(&saved));
        }
        if fs::metadata(&state).is_err() {
            return None;
        }
        native_trace("open: seed read; constructing NDS");
        // A new machine runs on the interpreter until the host asks otherwise (#153).
        // A diagnostic starts it on the recompiler, to run a whole tool on it.
        kssu_recompile = std::env::var_os("KSSU_RECOMPILER").is_some() as u32;
        kssu_recompile_flush = 0;
        let mut nds = Nds::new();
        native_trace("open: NDS constructed");
        nds.set_audio_output_skew(1.0);
        nds.set_nds_cart(&rom, None);
        native_trace("open: cartridge loaded");
        nds.set_time(
            Utc.with_ymd_and_hms(2026, 10, 3, 12, 0, 0)
                .single()
                .unwrap(),
        );
        if nds.needs_direct_boot() {
            nds.setup_direct_boot("KSSU_BRIDGE".to_owned());
        }
        nds.start();
        // A diagnostic can keep the frame-end step to compare the two (#134).
        kssu_vblank_step = std::env::var_os("KSSU_STEP_AT_FRAME_END").is_none() as u32;
        native_trace("open: NDS started; loading snapshot");
        // A new machine runs at the DS clock until the host asks otherwise.
        kssu_arm9_shift = 0;
        kssu_arm9_state = clock;
        if !nds.read_savestate_bytes(&mut saved) {
            return None;
        }
        restore_hook(hook);
        align_to_vblank(&mut nds);
        native_trace("open: snapshot loaded; returning handle");
        Some(Box::into_raw(Box::new(Bridge {
            nds,
            proxies: metadata.unwrap_or([Proxy::default(); PROXY_COUNT]),
            audio: VecDeque::new(),
            anchors: HashMap::new(),
            far_pending: None,
            far_shown: Vec::new(),
            unfinished_passes: 0,
            main_screen_only: false,
            idle_palette: None,
            proxies_fresh: false,
            proxy_tree: None,
        })) as *mut std::ffi::c_void)
    });
    run.ok().flatten().unwrap_or(ptr::null_mut())
}
#[no_mangle]
pub unsafe extern "C" fn kssu_close(handle: *mut std::ffi::c_void) {
    if !handle.is_null() {
        drop(Box::from_raw(handle as *mut Bridge));
    }
}
// The checked melonDS step patch (tools/nds_step_patch.py). The original waits
// for VBlank, then runs one logic pass: buttons, every actor, the camera, the
// sprite records and the shadow OAM. An emulator frame ends 71 scanlines after
// VBlank, inside that pass whenever it is longer (a helper or many objects make
// it so): Kirby and the camera were already updated and the sprites not yet
// built, and the terrain window was moved under them (#134). With the switch
// set (1) a source frame ends as VBlank begins instead, so everything between
// two frames meets the original between two passes. A state saved at either
// point loads and continues correctly (2: `align_to_vblank`).
extern "C" {
    static mut kssu_vblank_step: u32;
    // The same patch's clock stage (#150): the shift the next frame brings the
    // ARM9 clock to (1 the DS's 67 MHz, 2 twice that, 0 as it is), and the
    // shift a savestate's ARM9 timestamps count in, which the core writes when
    // it saves and takes when it loads.
    static mut kssu_arm9_shift: u32;
    static mut kssu_arm9_state: u32;
}
/// Follows the hook trailer of a state saved while the ARM9 ran at twice the DS
/// clock: the shift, then the magic. A state without it is at the DS clock.
const CLOCK_MAGIC: &[u8; 8] = b"KSSUCK01";
fn take_clock_trailer(data: &mut Vec<u8>) -> u32 {
    if data.len() < 12 + 16 || &data[data.len() - 8..] != CLOCK_MAGIC {
        return 1;
    }
    let shift = u32at(data, data.len() - 12);
    data.truncate(data.len() - 12);
    if shift == 2 {
        2
    } else {
        1
    }
}
/// Run the emulated ARM9 at `multiple` times the DS clock from the next source
/// frame on: 1 is the DS (67 MHz), which every machine starts at, and 2 is
/// 134 MHz, the clock the core has for a DSi. The original does the same work
/// in a pass either way; at 2 a pass that fills the DS's frame (a crowd, not
/// a cartridge load) ends sooner, so the original drops fewer frames. That is a
/// departure from the original machine (docs/GAMEPLAY_FIDELITY.md), asked for
/// by the host only. A state saved at either clock loads at either; one saved
/// and loaded at the same clock continues as if never saved.
#[no_mangle]
pub unsafe extern "C" fn kssu_arm9_clock(handle: *mut std::ffi::c_void, multiple: u32) -> i32 {
    if handle.is_null() || !(1..=2).contains(&multiple) {
        return -1;
    }
    // A compiled block counts its cycles at the clock it was compiled at (#153), so
    // a change discards the blocks; that happens after the clock changes.
    if kssu_arm9_shift != multiple {
        kssu_recompile_flush = 1;
    }
    // The clock shift is the multiple for both: 1 halves a system cycle, 2 quarters it.
    kssu_arm9_shift = multiple;
    0
}
/// Bring a machine just loaded from a state to where a step ends. A state saved
/// at the end of a frame (the seed, or one from before #134) has 192 scanlines
/// left before its VBlank, in which the original only waits; one saved by this
/// bridge is there already and stays as it is. Every step is then one whole
/// frame. The sound of those scanlines goes with the rest from before the load.
unsafe fn align_to_vblank(nds: &mut Nds) {
    if kssu_vblank_step == 0 {
        return;
    }
    kssu_vblank_step = 2;
    nds.run_frame();
    kssu_vblank_step = 1;
    while !nds.read_audio_output().is_empty() {}
}
#[no_mangle]
pub unsafe extern "C" fn kssu_step(handle: *mut std::ffi::c_void, mask: u16, frames: u32) -> i32 {
    if handle.is_null() || mask > 4095 || frames > 600 {
        return -1;
    }
    native_trace_once(0, "step: entered");
    let b = &mut *(handle as *mut Bridge);
    stale(b);
    if !mute_proxy_footsteps(b.nds.main_ram_mut())
        || !keep_proxies_alive_in_water(b.nds.main_ram_mut())
        || !quicken_helper_reaction(b.nds.main_ram_mut())
        || !keep_limited_abilities_off_mix_wheel(b.nds.main_ram_mut())
    {
        return -6;
    }
    if CODE_CHANGED {
        CODE_CHANGED = false;
        kssu_recompile_flush = 1;
    }
    b.nds.set_key_mask(ButtonMask::from_bits_retain(mask));
    b.nds.release_screen();
    for _ in 0..frames {
        native_trace_once(1, "step: preparing hurt carrier");
        prepare_hurt_proxy(b);
        native_trace_once(2, "step: applying proxy shapes");
        apply_proxy_shapes(b);
        native_trace_once(3, "step: executing source frame");
        b.anchors = actor_anchors(b.nds.main_ram_mut());
        remove_stray_plasma_aura(b);
        b.nds.run_frame();
        if pass_unfinished(b.nds.main_ram()) {
            b.unfinished_passes = b.unfinished_passes.saturating_add(1);
        }
        native_trace_once(20, "step: source frame returned; building off-screen OBJ");
        update_far_objects(b);
        native_trace_once(4, "step: source frame returned; reading PCM");
        for pair in b.nds.read_audio_output() {
            b.audio.extend(pair);
        }
        while b.audio.len() > 65536 {
            b.audio.pop_front();
        }
        native_trace_once(5, "step: PCM read; refreshing proxies");
        stale(b);
        refresh_proxies(b);
        native_trace_once(6, "step: completed");
    }
    0
}
#[no_mangle]
pub unsafe extern "C" fn kssu_read(
    handle: *mut std::ffi::c_void,
    address: u32,
    dest: *mut u8,
    len: usize,
) -> i32 {
    if handle.is_null() || dest.is_null() {
        return -1;
    }
    let Some(p) = off(address, len) else {
        return -2;
    };
    let b = &*(handle as *mut Bridge);
    ptr::copy_nonoverlapping(b.nds.main_ram().as_ptr().add(p), dest, len);
    0
}
#[no_mangle]
pub unsafe extern "C" fn kssu_write(
    handle: *mut std::ffi::c_void,
    address: u32,
    src: *const u8,
    len: usize,
) -> i32 {
    if handle.is_null() || src.is_null() {
        return -1;
    }
    let Some(p) = off(address, len) else {
        return -2;
    };
    let b = &mut *(handle as *mut Bridge);
    stale(b);
    ptr::copy_nonoverlapping(src, b.nds.main_ram_mut().as_mut_ptr().add(p), len);
    0
}
#[no_mangle]
pub unsafe extern "C" fn kssu_state(handle: *mut std::ffi::c_void, dest: *mut KssuState) -> i32 {
    if handle.is_null() || dest.is_null() {
        return -1;
    }
    let b = &*(handle as *mut Bridge);
    let r = b.nds.main_ram();
    let player = u32at(r, 0x49df0);
    let Some(p) = off(player, 0xa4) else {
        return -2;
    };
    let mut state = KssuState {
        frame: b.nds.current_frame(),
        player,
        x_raw: u32at(r, p + 0x80) as i32,
        y_raw: u32at(r, p + 0x84) as i32,
        vx_raw: u16at(r, p + 0x88) as i16,
        vy_raw: u16at(r, p + 0x8a) as i16,
        camera_x: u16at(r, 0x4222e) as i16,
        camera_y: u16at(r, 0x4222a) as i16,
        ability: r[0xbaf7b],
        kind: r[p + 0x16],
        flags: r[p + 0x14],
        facing_raw: r[p + 0x15],
        graphic: u32at(r, p + 0x70),
        animation: u32at(r, p + 0x24),
        ..Default::default()
    };
    for i in 0..16 {
        state.state_words[i] = u32at(r, p + 0x40 + i * 4);
    }
    *dest = state;
    0
}
#[no_mangle]
pub unsafe extern "C" fn kssu_hits(
    handle: *mut std::ffi::c_void,
    dest: *mut KssuHit,
    capacity: usize,
) -> i32 {
    if handle.is_null() || dest.is_null() {
        return -1;
    }
    let r = (*(handle as *mut Bridge)).nds.main_ram();
    let mut count = 0;
    for slot in 0..32 {
        let mut address = u32at(r, 0x76f00 + slot * 4);
        let mut seen = std::collections::HashSet::new();
        while address != 0 {
            if !seen.insert(address) || seen.len() > 256 {
                return -2;
            }
            let Some(p) = off(address, 0x23a) else {
                return -3;
            };
            if count == capacity {
                return count as i32;
            }
            let definition = u32at(r, p + 0x14);
            let mut hit = KssuHit {
                address,
                owner: u32at(r, p + 0x38),
                slot: slot as u32,
                definition,
                ..Default::default()
            };
            for i in 0..4 {
                hit.bounds[i] = u16at(r, p + 0x24 + i * 2) as i16;
            }
            if let Some(d) = off(definition, 28) {
                for i in 0..14 {
                    hit.words[i] = u16at(r, d + i * 2);
                }
            }
            *dest.add(count) = hit;
            count += 1;
            address = u32at(r, p);
        }
    }
    count as i32
}
#[no_mangle]
pub unsafe extern "C" fn kssu_framebuffer(
    handle: *mut std::ffi::c_void,
    dest: *mut u8,
    len: usize,
    bottom: bool,
) -> i32 {
    if handle.is_null() || dest.is_null() || len != 256 * 192 * 4 {
        return -1;
    }
    let b = &mut *(handle as *mut Bridge);
    if b.nds
        .update_framebuffers(std::slice::from_raw_parts_mut(dest, len), bottom)
    {
        0
    } else {
        -2
    }
}
#[no_mangle]
pub unsafe extern "C" fn kssu_save_state(
    handle: *mut std::ffi::c_void,
    path: *const c_char,
) -> i32 {
    if handle.is_null() || path.is_null() {
        return -1;
    }
    let Ok(path) = CStr::from_ptr(path).to_str() else {
        return -2;
    };
    let b = &mut *(handle as *mut Bridge);
    let mut data = b.nds.savestate();
    let clock = kssu_arm9_state;
    append_metadata(&mut data, &b.proxies);
    append_hook_trailer(&mut data);
    if clock == 2 {
        data.extend(clock.to_le_bytes());
        data.extend(CLOCK_MAGIC);
    }
    if fs::write(path, data).is_ok() {
        0
    } else {
        -3
    }
}
#[no_mangle]
pub unsafe extern "C" fn kssu_read_gpu(
    handle: *mut std::ffi::c_void,
    address: u32,
    dest: *mut u8,
    len: usize,
) -> i32 {
    if handle.is_null() || dest.is_null() {
        return -1;
    }
    native_trace_once(7, "GPU: reading first ARM9 GPU range");
    let allowed = (address >= 0x04000000
        && address
            .checked_add(len as u32)
            .is_some_and(|e| e <= 0x04001070))
        || (address >= 0x05000000
            && address
                .checked_add(len as u32)
                .is_some_and(|e| e <= 0x07000800));
    if !allowed || len > 0x40000 {
        return -2;
    }
    let b = &mut *(handle as *mut Bridge);
    // The host reads all of engine A's OBJ VRAM, the OAM and the OBJ palette on
    // every source frame. Whole 8-byte words of them are copied as a block (the
    // same accessors and power test as the bus read, see
    // tools/nds_graphics_patch.py) instead of one bus read a byte (#123, #165).
    let end = address + len as u32;
    let whole = (address | end) & 7 == 0;
    let out = std::slice::from_raw_parts_mut(dest, len);
    let block = whole
        && if address >= OBJ_VRAM && end <= OBJ_VRAM + OBJ_VRAM_SIZE {
            b.nds.obj_vram(address - OBJ_VRAM, out)
        } else {
            gpu_block(address, end) && b.nds.gpu_memory(address, out)
        };
    if !block {
        for i in 0..len {
            *dest.add(i) = b.nds.bus_read8(address + i as u32);
        }
    }
    native_trace_once(8, "GPU: first read completed");
    0
}
const OBJ_VRAM: u32 = 0x06400000;
const OBJ_VRAM_SIZE: u32 = 0x40000;
const PALETTES: u32 = 0x05000000;
const OAM: u32 = 0x07000000;
/// Inside the 2 KB of palettes or of OAM, which `Copy_GpuMemory` copies as a block.
fn gpu_block(address: u32, end: u32) -> bool {
    [PALETTES, OAM].iter().any(|&base| address >= base && end <= base + 0x800)
}
/// Leave engine B's 2D drawing out of every frame; its half of the framebuffer
/// is black. The host shows neither DS screen and reads only engine A (OBJ
/// tiles, OAM, palettes and the layer capture), so it turns this on. Tools and
/// tests that read the framebuffer keep the default. The emulated machine, its
/// save states and audio are the same either way (bridge test `main_screen_only`).
#[no_mangle]
pub unsafe extern "C" fn kssu_main_screen_only(handle: *mut std::ffi::c_void, enabled: bool) -> i32 {
    if handle.is_null() {
        return -1;
    }
    let b = &mut *(handle as *mut Bridge);
    if !b.nds.set_main_screen_only(enabled) {
        return -2;
    }
    b.main_screen_only = enabled;
    0
}
/// Leave engine A's backgrounds and composition out of every scanline the host
/// would learn nothing from. The host reads that drawing only through
/// `kssu_read_layers`, to find a palette or colour effect over the whole frame;
/// a scanline of an enabled, unblanked unit with no colour effect control whose
/// standard BG palette equals `palette` (512 bytes, the palette the host
/// compares against) reads as "no effect" there. Sprites, and a scanline a
/// display capture reads, are always drawn, so the emulated machine and its
/// save states are the same either way (bridge test `skip_idle_main`). Null
/// turns it off, which is the default.
/// The top framebuffer is black on such scanlines and the layer capture keeps
/// what an earlier frame drew: ask `kssu_main_frame` before reading either.
/// `KSSU_DRAW_EVERY_FRAME` in the environment keeps every scanline drawn.
#[no_mangle]
pub unsafe extern "C" fn kssu_skip_idle_main(
    handle: *mut std::ffi::c_void,
    palette: *const u8,
    len: usize,
) -> i32 {
    if handle.is_null() || (!palette.is_null() && len != 512) {
        return -1;
    }
    let b = &mut *(handle as *mut Bridge);
    let mut reference = None;
    if !palette.is_null() && std::env::var_os("KSSU_DRAW_EVERY_FRAME").is_none() {
        let mut copy = [0u8; 512];
        copy.copy_from_slice(std::slice::from_raw_parts(palette, 512));
        reference = Some(copy);
    }
    if !b.nds.set_skip_idle_main(reference.as_ref()) {
        return -2;
    }
    b.idle_palette = reference;
    0
}
/// How engine A's last frame was drawn (the frame whose 192 scanlines ended
/// at the latest VBlank): 0 every scanline, so `kssu_read_layers` holds that
/// frame; 1 none, the frame has no effect; 2 some of each, which is a frame
/// whose palette or colour effect changed part of the way down.
#[no_mangle]
pub unsafe extern "C" fn kssu_main_frame(handle: *mut std::ffi::c_void) -> i32 {
    if handle.is_null() {
        return -1;
    }
    (*(handle as *mut Bridge)).nds.main_frame() as i32
}
fn put32(r: &mut [u8], p: usize, v: u32) {
    r[p..p + 4].copy_from_slice(&v.to_le_bytes());
}
/// Set when the bridge changed a word of the original's code in RAM. It writes
/// past the emulated bus, so the recompiler has to be told (#153).
static mut CODE_CHANGED: bool = false;
fn code32(r: &mut [u8], p: usize, v: u32) {
    if u32at(r, p) != v {
        unsafe { CODE_CHANGED = true };
        put32(r, p, v);
    }
}
/// Run the original on the emulator's recompiler (`true`) or its interpreter
/// (`false`, which every machine starts on) from the next source frame on. The
/// recompiler does the work in about three fifths of the time, but an interrupt
/// or a scheduled event lands at the end of a compiled block instead of after
/// the instruction, so its timing is not exactly the interpreter's and a load
/// can end a frame later (docs/NDS_BRIDGE.md). While a call is armed the ARM9 runs on the
/// interpreter, where the call hook is. Process-wide, like the hook.
#[no_mangle]
pub unsafe extern "C" fn kssu_recompiler(handle: *mut std::ffi::c_void, enabled: bool) -> i32 {
    if handle.is_null() {
        return -1;
    }
    kssu_recompile = enabled as u32;
    0
}
/// Tell the recompiler that code in RAM changed: `kssu_write` writes past the
/// emulated bus, and a host that changes instructions with it calls this
/// before the next step. Data needs no call. The bridge's own patches do it.
#[no_mangle]
pub unsafe extern "C" fn kssu_code_changed(handle: *mut std::ffi::c_void) -> i32 {
    if handle.is_null() {
        return -1;
    }
    kssu_recompile_flush = 1;
    0
}
fn actor_nodes(r: &[u8]) -> Vec<usize> {
    let mut nodes = Vec::new();
    for slot in 0..7 {
        let mut a = u32at(r, 0x49dec + slot * 4);
        let mut seen = std::collections::HashSet::new();
        while a != 0 {
            let Some(p) = off(a, 0x8c) else {
                break;
            };
            if !seen.insert(a) || seen.len() > 256 {
                break;
            }
            nodes.push(p);
            a = u32at(r, p);
        }
    }
    nodes
}
/// The points an attached actor can be drawn from: the actor's drawn
/// position (+0x78/+0x7C) and its sprite anchor (the point 0x020A8F90 returns).
fn anchor_points(r: &[u8], p: usize) -> [(i32, i32); 2] {
    let drawn = (u32at(r, p + 0x78) as i32, u32at(r, p + 0x7c) as i32);
    let sprite = off(u32at(r, p + 0x40).wrapping_add(0x748), 8)
        .map_or(drawn, |s| (u32at(r, s) as i32, u32at(r, s + 4) as i32));
    [drawn, sprite]
}
fn actor_anchors(r: &[u8]) -> HashMap<u32, [(i32, i32); 2]> {
    actor_nodes(r)
        .into_iter()
        .map(|p| (BASE + p as u32, anchor_points(r, p)))
        .collect()
}
/// Whether an actor's +0x80/+0x84 is an offset from its parent rather than a
/// position. The original's attach helpers (0x0200F208 and its kin, 0x020C838C,
/// 0x020CD368) draw such an actor at a parent point plus that offset (plus its
/// whole pixels, mirrored with facing, for sprite bones), reading the point
/// before or after the parent moved that frame. Fire's and Plasma's head
/// emitters and many attack effects are attached this way.
fn parent_relative(r: &[u8], p: usize, before: &HashMap<u32, [(i32, i32); 2]>) -> bool {
    let (x, y) = (u32at(r, p + 0x78) as i32, u32at(r, p + 0x7c) as i32);
    let (ox, oy) = (u32at(r, p + 0x80) as i32, u32at(r, p + 0x84) as i32);
    let parent = u32at(r, p + 4);
    let Some(q) = off(parent, 0x8c) else {
        return false;
    };
    if (x, y) == (ox, oy) {
        return false;
    }
    let (bx, by) = (ox & !0xffff, oy & !0xffff);
    let earlier = before.get(&parent).into_iter().flatten().copied();
    anchor_points(r, q)
        .into_iter()
        .chain(earlier)
        .any(|(ax, ay)| {
            (x, y) == (ax.wrapping_add(ox), ay.wrapping_add(oy))
                || ((x == ax.wrapping_add(bx) || x == ax.wrapping_sub(bx))
                    && y == ay.wrapping_add(by))
        })
}
fn descendants(r: &[u8], player: u32) -> Vec<(usize, bool)> {
    let nodes = actor_nodes(r);
    let mut own = std::collections::HashSet::from([player]);
    for &p in &nodes {
        if r[p + 0x16] == 56 {
            own.insert(BASE + p as u32);
        }
    }
    for _ in 0..8 {
        let old = own.len();
        for &p in &nodes {
            if own.contains(&u32at(r, p + 4)) {
                own.insert(BASE + p as u32);
            }
        }
        if own.len() == old {
            break;
        }
    }
    nodes
        .into_iter()
        .map(|p| (p, own.contains(&(BASE + p as u32))))
        .collect()
}
/// Replace the seeded 100x9 source map. Cells are original block IDs (0 empty,
/// 3 solid). The target position rebases the source's 16.16 coordinates only
/// (actors attached to a parent keep their offsets); velocities, controller
/// state, action timers and original physics are retained.
#[no_mangle]
pub unsafe extern "C" fn kssu_world(
    handle: *mut std::ffi::c_void,
    cells: *const u16,
    count: usize,
    target_x: i32,
    target_y: i32,
) -> i32 {
    native_trace_once(9, "world: entered");
    if handle.is_null() || cells.is_null() || count != 900 {
        return -1;
    }
    let b = &mut *(handle as *mut Bridge);
    stale(b);
    let r = b.nds.main_ram_mut();
    if !expand_scene(r) {
        return -5;
    }
    if !extend_hit_pool(r) {
        return -6;
    }
    native_trace_once(10, "world: scene allocation validated");
    let player = u32at(r, 0x49df0);
    let Some(p) = off(player, 0x8c) else {
        return -2;
    };
    if u32at(r, 0x9f36c) != 0x0226ce40 || u16at(r, 0x9f3b2) != 100 || u16at(r, 0x9f3b4) != 9 {
        return -3;
    }
    let cells = std::slice::from_raw_parts(cells, count);
    if cells.iter().any(|&v| v >= 138) {
        return -4;
    }
    let dx = target_x.wrapping_sub(u32at(r, p + 0x80) as i32);
    let dy = target_y.wrapping_sub(u32at(r, p + 0x84) as i32);
    let nodes = descendants(r, player);
    let relative: Vec<bool> = nodes
        .iter()
        .map(|&(node, _)| parent_relative(r, node, &b.anchors))
        .collect();
    for ((node, owned), relative) in nodes.into_iter().zip(relative) {
        let proxy_slot = proxy_slot_for(r, node);
        let proxy = proxy_slot.is_some_and(|slot| b.proxies[slot].flags != 0);
        if (proxy && u32at(r, node + 0x30) == 0x0215acf8) || u32at(r, node + 0x2c) == 0x0214dc70 {
            continue;
        }
        if owned || proxy || proxy_slot.is_none() {
            // Drawn positions move with the window, so an attached actor that
            // reads its parent's before the parent runs again stays on it;
            // offsets from a parent stay as they are.
            let drawn = u32at(r, node + 0x78) == u32at(r, node + 0x80)
                && u32at(r, node + 0x7c) == u32at(r, node + 0x84);
            let mut fields = Vec::with_capacity(2);
            if relative || drawn {
                fields.push(0x78);
            }
            if !relative {
                fields.push(0x80);
            }
            for at in fields {
                let x = (u32at(r, node + at) as i32).wrapping_add(dx);
                let y = (u32at(r, node + at + 4) as i32).wrapping_add(dy);
                put32(r, node + at, x as u32);
                put32(r, node + at + 4, y as u32);
            }
        } else {
            retire_proxy(r, node);
        }
    }
    // The source camera scrolls from its own position (i16 x at 0x0209F3A8)
    // toward Kirby, at most 16 px a frame, and the original draws only what is
    // on its 256-pixel screen. Move it with the window; after a placement that
    // leaves Kirby away from the centre (he stays within about 24 px of x 128),
    // frame him at once instead of drawing him off screen while it catches up.
    let kirby_x = target_x.wrapping_add(0x8000) >> 16;
    let camera = (u16at(r, 0x9f3a8) as i16 as i32).wrapping_add(dx.wrapping_add(0x8000) >> 16);
    let camera = if (96..=160).contains(&(kirby_x - camera)) { camera } else { kirby_x - 128 };
    r[0x9f3a8..0x9f3aa].copy_from_slice(&(camera as i16).to_le_bytes());
    // The original draws from its own copy of the camera (i16 x at 0x0204222E,
    // y at 0x0204222A), which a logic pass sets after it has moved the camera
    // and `kssu_state` reports. A pass that leaves the scene as it is (the
    // original holds it while Kirby takes an ability) does not set it again:
    // moved with everything else, Kirby stays where he was drawn, and the
    // position the host reads for him stays the one on screen (#134).
    for (q, d) in [(0x4222e, dx), (0x4222a, dy)] {
        let copy = (u16at(r, q) as i16).wrapping_add((d.wrapping_add(0x8000) >> 16) as i16);
        r[q..q + 2].copy_from_slice(&copy.to_le_bytes());
    }
    // Terrain probes cache absolute sample positions across frames (ten 28-byte records).
    for i in 0..10 {
        let q = 0xbb0b4 + i * 28;
        let x = (u16at(r, q) as i16).wrapping_add((dx >> 16) as i16);
        let y = (u16at(r, q + 2) as i16).wrapping_add((dy >> 16) as i16);
        r[q..q + 2].copy_from_slice(&x.to_le_bytes());
        r[q + 2..q + 4].copy_from_slice(&y.to_le_bytes());
    }
    for q in [0xbb6b0, 0xbb6f8] {
        let x = (u32at(r, q) as i32).wrapping_add(dx);
        let y = (u32at(r, q + 4) as i32).wrapping_add(dy);
        put32(r, q, x as u32);
        put32(r, q + 4, y as u32);
    }
    // The expanded original scene spawn records are replaced by host mobs.
    let basic = basic_base(r);
    let records = record_base(r);
    for i in 0..PROXY_COUNT {
        r[basic + i * 8 + 4..basic + i * 8 + 8].copy_from_slice(&[0xff, 0x7f, 0xff, 0x7f]);
        if b.proxies[i].flags == 0 {
            r[records + i * 52] = 0;
        }
    }
    // Bridge-reserved blocks retain source terrain behavior with host geometry.
    // 130 water,131 one-way,132/133 original45-degree slopes,134 ladder; solids remain3.
    r[0x26cda4 + 130..0x26cda4 + 135].copy_from_slice(&[0x80, 0x2f, 0x40, 0x41, 0x0e]);
    for (i, &cell) in cells.iter().enumerate() {
        r[0x26ce40 + i * 2..0x26ce42 + i * 2].copy_from_slice(&cell.to_le_bytes());
    }
    // Original room doors cache their sparkle positions outside the actor tree.
    // Registration 0x020821E0 accepts only door terrain (essence 0x0F), and
    // drawing 0x0208B3CC skips a zero type in this sixteen-record registry.
    // The host owns room exits; retain all actor/ability particle systems.
    for slot in 0..16 {
        r[0x9fbec + slot * 8..0x9fbee + slot * 8].fill(0);
    }
    0
}
/// Move only the player by a 16.16 delta inside the current window, as if the
/// original controller had moved him: his cached terrain probes and the two
/// linked position records that `kssu_world` also moves go with him, and the
/// source camera follows him by itself. Thrown objects, effects, the helper and
/// enemies stay where they are; objects attached to Kirby are placed again by
/// the original next frame. `kssu_world` still moves everything together when
/// the window itself moves.
#[no_mangle]
pub unsafe extern "C" fn kssu_move_player(handle: *mut std::ffi::c_void, dx: i32, dy: i32) -> i32 {
    if handle.is_null() {
        return -1;
    }
    let b = &mut *(handle as *mut Bridge);
    stale(b);
    let r = b.nds.main_ram_mut();
    let player = u32at(r, 0x49df0);
    let Some(p) = off(player, 0x8c) else {
        return -2;
    };
    let old_x = u32at(r, p + 0x80) as i32;
    let old_y = u32at(r, p + 0x84) as i32;
    let new_x = old_x.wrapping_add(dx);
    let new_y = old_y.wrapping_add(dy);
    put32(r, p + 0x80, new_x as u32);
    put32(r, p + 0x84, new_y as u32);
    // The probes hold whole-pixel sample positions around Kirby.
    let px = (new_x >> 16).wrapping_sub(old_x >> 16) as i16;
    let py = (new_y >> 16).wrapping_sub(old_y >> 16) as i16;
    for i in 0..10 {
        let q = 0xbb0b4 + i * 28;
        let x = (u16at(r, q) as i16).wrapping_add(px);
        let y = (u16at(r, q + 2) as i16).wrapping_add(py);
        r[q..q + 2].copy_from_slice(&x.to_le_bytes());
        r[q + 2..q + 4].copy_from_slice(&y.to_le_bytes());
    }
    for q in [0xbb6b0, 0xbb6f8] {
        let x = (u32at(r, q) as i32).wrapping_add(dx);
        let y = (u32at(r, q + 4) as i32).wrapping_add(dy);
        put32(r, q, x as u32);
        put32(r, q + 4, y as u32);
    }
    0
}
/// The original CPU helper (the DS X button's partner) and its controller block.
/// Like Kirby it is an actor of kind 56 running the player update 0x020B2714, with
/// its own controller block (Kirby's is at 0x020BAF68). Helpers of every ability
/// share this block, so there is one helper at a time.
const HELPER_RECORD: u32 = 0x020bb784;
#[repr(C)]
#[derive(Default, Copy, Clone)]
pub struct KssuHelper {
    pub x_raw: i32,
    pub y_raw: i32,
    pub vx_raw: i16,
    pub vy_raw: i16,
    /// The helper controller's action code, in Kirby's codes (`KirbyActions`).
    pub action: u16,
    pub reserved: u16,
}
fn helper_node(r: &[u8]) -> Option<usize> {
    actor_nodes(r).into_iter().find(|&p| {
        r[p + 0x16] == 56
            && u32at(r, p + 0x40) == HELPER_RECORD
            && u32at(r, p + 0x30) == PLAYER_UPDATE
    })
}
/// Where the original helper is, in the same 16.16 source coordinates as
/// `kssu_state`. Returns -2 while Kirby has no helper.
#[no_mangle]
pub unsafe extern "C" fn kssu_helper_state(
    handle: *mut std::ffi::c_void,
    dest: *mut KssuHelper,
) -> i32 {
    if handle.is_null() || dest.is_null() {
        return -1;
    }
    let r = (&*(handle as *mut Bridge)).nds.main_ram();
    let Some(p) = helper_node(r) else {
        return -2;
    };
    let record = (HELPER_RECORD - BASE) as usize;
    *dest = KssuHelper {
        x_raw: u32at(r, p + 0x80) as i32,
        y_raw: u32at(r, p + 0x84) as i32,
        vx_raw: u16at(r, p + 0x88) as i16,
        vy_raw: u16at(r, p + 0x8a) as i16,
        action: u16at(r, record + 0x18),
        reserved: 0,
    };
    0
}
/// Move only the helper by a 16.16 delta, as `kssu_move_player` does for Kirby:
/// its cached terrain probes and linked position records go with it, and the
/// original places everything attached to it again next frame. Returns -2
/// while Kirby has no helper.
#[no_mangle]
pub unsafe extern "C" fn kssu_move_helper(handle: *mut std::ffi::c_void, dx: i32, dy: i32) -> i32 {
    if handle.is_null() {
        return -1;
    }
    let b = &mut *(handle as *mut Bridge);
    stale(b);
    let r = b.nds.main_ram_mut();
    let Some(p) = helper_node(r) else {
        return -2;
    };
    let old_x = u32at(r, p + 0x80) as i32;
    let old_y = u32at(r, p + 0x84) as i32;
    let new_x = old_x.wrapping_add(dx);
    let new_y = old_y.wrapping_add(dy);
    put32(r, p + 0x80, new_x as u32);
    put32(r, p + 0x84, new_y as u32);
    let record = (HELPER_RECORD - BASE) as usize;
    let px = (new_x >> 16).wrapping_sub(old_x >> 16) as i16;
    let py = (new_y >> 16).wrapping_sub(old_y >> 16) as i16;
    for i in 0..10 {
        let q = record + 0x14c + i * 28;
        let x = (u16at(r, q) as i16).wrapping_add(px);
        let y = (u16at(r, q + 2) as i16).wrapping_add(py);
        r[q..q + 2].copy_from_slice(&x.to_le_bytes());
        r[q + 2..q + 4].copy_from_slice(&y.to_le_bytes());
    }
    for q in [record + 0x748, record + 0x790] {
        let x = (u32at(r, q) as i32).wrapping_add(dx);
        let y = (u32at(r, q + 4) as i32).wrapping_add(dy);
        put32(r, q, x as u32);
        put32(r, q + 4, y as u32);
    }
    0
}
/// Inspect original attack graph. Bit0=active inhale hit, bit1=swallowed contents.
#[no_mangle]
pub unsafe extern "C" fn kssu_action_flags(handle: *mut std::ffi::c_void) -> i32 {
    if handle.is_null() {
        return -1;
    }
    let b = &*(handle as *mut Bridge);
    let r = b.nds.main_ram();
    let player = u32at(r, 0x49df0);
    let mut flags = 0;
    let mut a = u32at(r, 0x76f00 + 6 * 4);
    let mut seen = std::collections::HashSet::new();
    while let Some(p) = off(a, 0x3c) {
        if !seen.insert(a) || seen.len() > 256 {
            break;
        }
        if u32at(r, p + 0x38) == player && r[0xbaf7b] == 1 {
            flags |= 1;
        }
        a = u32at(r, p);
    }
    if r[0xbb211] != 0 {
        flags |= 2;
    }
    flags
}
/// Insert consumed host content into the ORIGINAL inhale state. Copy loading and
/// transformation happen later through the original DOWN swallow action.
#[no_mangle]
pub unsafe extern "C" fn kssu_feed(handle: *mut std::ffi::c_void, ability: u8) -> i32 {
    if handle.is_null() || !(1..=25).contains(&ability) {
        return -1;
    }
    if kssu_action_flags(handle) & 3 != 1 {
        return -2;
    }
    let b = &mut *(handle as *mut Bridge);
    stale(b);
    let r = b.nds.main_ram_mut();
    put32(r, 0xbb210, ((ability as u32) << 24) | 0x100);
    0
}
#[no_mangle]
pub unsafe extern "C" fn kssu_player_owns(handle: *mut std::ffi::c_void, address: u32) -> i32 {
    if handle.is_null() {
        return -1;
    }
    let r = (&*(handle as *mut Bridge)).nds.main_ram();
    let player = u32at(r, 0x49df0);
    if address == player {
        return 1;
    }
    descendants(r, player)
        .iter()
        .any(|&(p, owned)| owned && BASE + p as u32 == address) as i32
}
/// Whether an original object is the CPU helper or descends from it (a thrown
/// bomb, a cutter, an attack effect). `kssu_player_owns` counts the helper as
/// Kirby's, so this is how a hit is told apart. Returns 0 while Kirby has no
/// helper.
#[no_mangle]
pub unsafe extern "C" fn kssu_helper_owns(handle: *mut std::ffi::c_void, address: u32) -> i32 {
    if handle.is_null() {
        return -1;
    }
    let r = (&*(handle as *mut Bridge)).nds.main_ram();
    let Some(root) = helper_node(r).map(|p| BASE + p as u32) else {
        return 0;
    };
    // Each object stores its parent at +4; attack effects nest only a few levels.
    let mut at = address;
    for _ in 0..9 {
        if at == root {
            return 1;
        }
        let Some(p) = off(at, 8) else {
            break;
        };
        at = u32at(r, p + 4);
        if at == 0 {
            break;
        }
    }
    0
}
fn proxy_slot_for(r: &[u8], p: usize) -> Option<usize> {
    if r[p + 0x16] != 72 {
        return None;
    }
    let d = off(u32at(r, p + 0x40), 0x90)?;
    let sp = u32at(r, d + 0x8c);
    let base = BASE + record_base(r) as u32;
    let count = r[0x9f3e8] as u32;
    if sp < base || sp >= base + count * 52 || (sp - base) % 52 != 0 {
        return None;
    }
    Some(((sp - base) / 52) as usize)
}
fn proxy_nodes(r: &[u8]) -> [Option<usize>; PROXY_COUNT] {
    let mut out = [None; PROXY_COUNT];
    let player = u32at(r, 0x49df0);
    for (p, _) in descendants(r, player) {
        if let Some(slot) = proxy_slot_for(r, p) {
            out[slot] = Some(p);
        }
    }
    out
}
/// The proxy slots' objects, walked once until the machine changes (#160).
fn proxy_tree(b: &mut Bridge) -> [Option<usize>; PROXY_COUNT] {
    if let Some(tree) = b.proxy_tree {
        return tree;
    }
    let tree = proxy_nodes(b.nds.main_ram());
    b.proxy_tree = Some(tree);
    tree
}
/// The machine may have changed: refresh the proxies and walk the actor lists again.
fn stale(b: &mut Bridge) {
    b.proxies_fresh = false;
    b.proxy_tree = None;
}
fn refresh_proxies(b: &mut Bridge) {
    if b.proxies_fresh {
        return;
    }
    b.proxies_fresh = true;
    let nodes = proxy_tree(b);
    let frame = b.nds.current_frame();
    let r = b.nds.main_ram();
    for slot in 0..PROXY_COUNT {
        let q = &mut b.proxies[slot];
        if q.flags == 0 {
            continue;
        }
        if q.flags & 64 != 0 {
            if nodes[slot].is_none() {
                *q = Proxy::default();
            }
            continue;
        }
        if let Some(p) = nodes[slot] {
            let d = off(u32at(r, p + 0x40), 0x90).unwrap();
            let callback = u32at(r, p + 0x30);
            if q.normal_update == 0 {
                q.normal_update = callback;
                q.born_frame = frame;
            }
            let was_controlled =
                callback != q.normal_update || r[d + 0xb] & 1 != 0 || r[p + 0x15] & 0x10 != 0;
            q.entity = BASE + p as u32;
            q.flags = 1
                | if was_controlled { 2 } else { 0 }
                | if callback == 0x0215acf8 { 32 } else { 0 }
                | if frame == q.born_frame { 16 } else { 0 }
                | if u32at(r, p + 0x34) == 0x0216394c {
                    128
                } else {
                    0
                };
            // +78/+7c are the original WORLD render positions, even during sucking/grabs.
            let visible = if r[p + 0x15] & 0x10 != 0 {
                off(u32at(r, p + 0xc), 0x9c)
                    .filter(|&c| r[c + 0x16] == 72)
                    .unwrap_or(p)
            } else {
                p
            };
            q.x = u32at(r, visible + 0x78) as i32;
            q.y = u32at(r, visible + 0x7c) as i32;
            if frame != q.born_frame {
                let health_data = off(u32at(r, visible + 0x40), 0x90).unwrap_or(d);
                q.hp = u16at(r, health_data + 0x10) as i16 as i32;
                // Collisions write the group HP before the enemy controller
                // copies it into canonical HP on a later recoil tick. Export
                // that pending damage now, before the host health sync runs.
                for field in [0x34, 0x38] {
                    if let Some(group) = off(u32at(r, health_data + field), 0x240) {
                        if u16at(r, group + 0x23a) != 0 {
                            q.hp = q.hp.min((u16at(r, group + 0x23c) as i16 as i32).max(0));
                        }
                    }
                }
            }
        } else if q.entity != 0 {
            q.flags = if q.flags & 32 != 0 && r[0xbb211] != 0 {
                4
            } else {
                8 | (q.flags & 128)
            };
            q.entity = 0;
        }
    }
}
#[repr(C)]
#[derive(Default, Copy, Clone)]
pub struct KssuProxy {
    pub entity: u32,
    pub flags: u32,
    pub x_raw: i32,
    pub y_raw: i32,
    pub hp: i32,
    pub update: u32,
}
/// Bind a host mob to an expanded original scene enemy spawn slot (1..31).
/// active=false schedules native destruction before slot reuse.
/// Source capture/grab states retain their own position until the action finishes.
#[no_mangle]
pub unsafe extern "C" fn kssu_proxy_set(
    handle: *mut std::ffi::c_void,
    slot: u32,
    x: i32,
    y: i32,
    hp: i32,
    active: bool,
) -> i32 {
    if handle.is_null() || !(1..PROXY_COUNT as u32).contains(&slot) || hp > 32767 {
        return -1;
    }
    let slot = slot as usize;
    let b = &mut *(handle as *mut Bridge);
    refresh_proxies(b);
    // The writes below change positions, health and records, which the refresh
    // reads, but not which object is in which slot. The first call enlarges the
    // scene's record table, which does (#160).
    b.proxies_fresh = false;
    let expanded = b.nds.main_ram()[0x9f3e8] as usize == PROXY_COUNT;
    if !expand_scene(b.nds.main_ram_mut()) {
        return -4;
    }
    if !expanded {
        b.proxy_tree = None;
    }
    let node = proxy_tree(b)[slot];
    let r = b.nds.main_ram_mut();
    if !active {
        b.proxies[slot] = Proxy::default();
        let records = record_base(r);
        r[records + slot * 52] = 0;
        if let Some(p) = node {
            retire_proxy(r, p);
            b.proxies[slot] = Proxy {
                entity: BASE + p as u32,
                flags: 64 | 16,
                ..Default::default()
            };
        }
        return 0;
    }
    if b.proxies[slot].flags & 64 != 0 {
        return 0;
    }
    if let Some(p) = node {
        let q = &mut b.proxies[slot];
        if q.flags == 0 {
            q.flags = 1;
            q.normal_update = u32at(r, p + 0x30);
            q.entity = BASE + p as u32;
        }
        if q.flags & 2 == 0 || q.options & 0x100 != 0 {
            put32(r, p + 0x80, x as u32);
            put32(r, p + 0x84, y as u32);
            put32(r, p + 0x78, x as u32);
            put32(r, p + 0x7c, y as u32);
        }
        set_entity_health(r, p, hp);
        if r[p + 0x15] & 0x10 != 0 {
            if let Some(clone) = off(u32at(r, p + 0xc), 0x9c).filter(|&c| r[c + 0x16] == 72) {
                set_entity_health(r, clone, hp);
            }
        }
    } else {
        if b.proxies[slot].flags & 12 != 0 {
            return -3;
        } // Host must acknowledge completed lifecycle before reusing.
        let q = record_base(r) + slot * 52;
        r[q..q + 52].fill(0xff);
        r[q..q + 5].copy_from_slice(&[1, 1, 0, 0, 2]);
        r[q + 8] = 0;
        r[q + 10..q + 12].copy_from_slice(&((x >> 16) as i16).to_le_bytes());
        r[q + 12..q + 14].copy_from_slice(&((y >> 16) as i16).to_le_bytes());
        b.proxies[slot] = Proxy {
            flags: 16,
            x,
            y,
            hp,
            width: b.proxies[slot].width,
            height: b.proxies[slot].height,
            options: b.proxies[slot].options,
            ..Default::default()
        };
    }
    0
}
#[no_mangle]
pub unsafe extern "C" fn kssu_proxy_state(
    handle: *mut std::ffi::c_void,
    slot: u32,
    dest: *mut KssuProxy,
) -> i32 {
    if handle.is_null() || dest.is_null() || !(1..PROXY_COUNT as u32).contains(&slot) {
        return -1;
    }
    let b = &mut *(handle as *mut Bridge);
    refresh_proxies(b);
    let q = b.proxies[slot as usize];
    *dest = KssuProxy {
        entity: q.entity,
        flags: q.flags,
        x_raw: q.x,
        y_raw: q.y,
        hp: q.hp,
        update: q.normal_update,
    };
    0
}
#[no_mangle]
pub unsafe extern "C" fn kssu_set_swallowed_ability(
    handle: *mut std::ffi::c_void,
    ability: u8,
) -> i32 {
    if handle.is_null() || !(1..=25).contains(&ability) {
        return -1;
    }
    if kssu_action_flags(handle) & 2 == 0 {
        return -2;
    }
    let b = &mut *(handle as *mut Bridge);
    stale(b);
    b.nds.main_ram_mut()[0xbb213] = ability;
    0
}
#[no_mangle]
pub unsafe extern "C" fn kssu_load_state(
    handle: *mut std::ffi::c_void,
    path: *const c_char,
) -> i32 {
    if handle.is_null() || path.is_null() {
        return -1;
    }
    let Ok(path) = CStr::from_ptr(path).to_str() else {
        return -2;
    };
    let Ok(mut data) = fs::read(path) else {
        return -3;
    };
    if data.len() < 16 || data.get(..4) != Some(b"MELN") {
        return -4;
    }
    let clock = take_clock_trailer(&mut data);
    let hook = take_hook_trailer(&mut data);
    let metadata = read_metadata(&data);
    if metadata.is_some() {
        data.truncate(data.len() - metadata_len(&data));
    }
    let b = &mut *(handle as *mut Bridge);
    stale(b);
    native_trace("load: restoring source snapshot");
    kssu_arm9_state = clock;
    if b.nds.read_savestate_bytes(&mut data) {
        native_trace("load: source snapshot restored");
        b.proxies = metadata.unwrap_or([Proxy::default(); PROXY_COUNT]);
        b.audio.clear();
        b.anchors.clear();
        b.far_pending = None;
        b.far_shown.clear();
        restore_hook(hook);
        if b.main_screen_only {
            b.nds.set_main_screen_only(true);
        }
        if let Some(palette) = b.idle_palette {
            b.nds.set_skip_idle_main(Some(&palette));
        }
        align_to_vblank(&mut b.nds);
        0
    } else {
        -5
    }
}

/// Drain original DS audio, signed16-bit interleaved stereo at48000Hz.
#[no_mangle]
pub unsafe extern "C" fn kssu_read_audio(
    handle: *mut std::ffi::c_void,
    dest: *mut i16,
    capacity: usize,
) -> i32 {
    if handle.is_null() || dest.is_null() || capacity > 65536 {
        return -1;
    }
    let b = &mut *(handle as *mut Bridge);
    native_trace_once(11, "audio: draining original PCM");
    let count = b.audio.len().min(capacity) & !1;
    for i in 0..count {
        *dest.add(i) = b.audio.pop_front().unwrap();
    }
    count as i32
}

// Run the original entity-destruction entry point on its normal update turn.
// It marks the entity for the source scheduler, which invokes its destructor and
// frees hitboxes/AI allocations; no linked-list surgery or stale-AI reuse.
fn retire_proxy(r: &mut [u8], p: usize) {
    r[p + 0x14] |= 0x10;
    put32(r, p + 0x30, 0x0214d43c);
    put32(r, p + 0x80, (-4096i32 * 65536) as u32);
    put32(r, p + 0x84, (-4096i32 * 65536) as u32);
}

const META_MAGIC: &[u8; 8] = b"KSSUMD05";
fn append_metadata(data: &mut Vec<u8>, proxies: &[Proxy; PROXY_COUNT]) {
    for q in proxies {
        for v in [
            q.entity,
            q.normal_update,
            q.flags,
            q.hp as u32,
            q.x as u32,
            q.y as u32,
            q.born_frame,
            q.width as u32 | ((q.height as u32) << 16),
            q.options,
            q.original_definition,
        ] {
            data.extend(v.to_le_bytes());
        }
    }
    data.extend(META_MAGIC);
}
fn read_metadata(data: &[u8]) -> Option<[Proxy; PROXY_COUNT]> {
    let len = metadata_len(data);
    if len == 0 {
        return None;
    }
    let count = if &data[data.len() - 8..] == META_MAGIC || &data[data.len() - 8..] == b"KSSUMD04" {
        PROXY_COUNT
    } else {
        8
    };
    let stride = (len - 8) / count;
    let data = &data[data.len() - len..];
    let mut out = [Proxy::default(); PROXY_COUNT];
    for (i, q) in out.iter_mut().take(count).enumerate() {
        let p = i * stride;
        *q = Proxy {
            entity: u32at(data, p),
            normal_update: u32at(data, p + 4),
            flags: u32at(data, p + 8),
            hp: u32at(data, p + 12) as i32,
            x: u32at(data, p + 16) as i32,
            y: u32at(data, p + 20) as i32,
            born_frame: u32at(data, p + 24),
            width: if stride > 28 { u16at(data, p + 28) } else { 0 },
            height: if stride > 28 { u16at(data, p + 30) } else { 0 },
            options: if stride > 32 { u32at(data, p + 32) } else { 0 },
            original_definition: if stride > 36 { u32at(data, p + 36) } else { 0 },
        };
        if q.entity != 0 && off(q.entity, 0x9c).is_none() {
            return None;
        }
    }
    Some(out)
}

fn metadata_len(data: &[u8]) -> usize {
    if data.len() >= PROXY_COUNT * 40 + 8 && &data[data.len() - 8..] == META_MAGIC {
        PROXY_COUNT * 40 + 8
    } else if data.len() >= PROXY_COUNT * 36 + 8 && &data[data.len() - 8..] == b"KSSUMD04" {
        PROXY_COUNT * 36 + 8
    } else if data.len() >= 296 && &data[data.len() - 8..] == b"KSSUMD03" {
        296
    } else if data.len() >= 264 && &data[data.len() - 8..] == b"KSSUMD02" {
        264
    } else if data.len() >= 232 && &data[data.len() - 8..] == b"KSSUMD01" {
        232
    } else {
        0
    }
}
/// Set host enemy AABB size. Native proxy center is17 pixels above its foot;
/// hurtbox top=17-height and bottom=17 align arbitrary host sizes to that foot.
/// The native collision definition is copied into that already-spawned scene
/// record's unused parameter tail, so no arbitrary emulated heap is overwritten.
#[no_mangle]
pub unsafe extern "C" fn kssu_proxy_size(
    handle: *mut std::ffi::c_void,
    slot: u32,
    width: u16,
    height: u16,
) -> i32 {
    if handle.is_null()
        || !(1..PROXY_COUNT as u32).contains(&slot)
        || width == 0
        || height == 0
        || width > 1024
        || height > 1024
    {
        return -1;
    }
    let b = &mut *(handle as *mut Bridge);
    b.proxies[slot as usize].width = width;
    b.proxies[slot as usize].height = height;
    0
}
fn apply_proxy_shapes(b: &mut Bridge) {
    let r = b.nds.main_ram_mut();
    let nodes = proxy_nodes(r);
    for slot in 1..PROXY_COUNT {
        let q = b.proxies[slot];
        if q.flags & 1 == 0 || q.flags & 64 != 0 {
            continue;
        }
        let Some(p) = nodes[slot] else {
            continue;
        };
        let Some(d) = off(u32at(r, p + 0x40), 0x90) else {
            continue;
        };
        for field in [0x34, 0x38] {
            if let Some(group) = off(u32at(r, d + field), 0x240) {
                if q.options & 255 != 0 {
                    r[group + 0x239] = (q.options & 255) as u8;
                }
                if q.options & 0x300 != 0 {
                    for index in 0..4 {
                        let node = group + 4 + index * 0x8c;
                        let mask = u16at(r, node + 0x46)
                            | if q.options & 0x100 != 0 { 0x53 } else { 0 }
                            | if q.options & 0x200 != 0 { 0x4000 } else { 0 };
                        r[node + 0x46..node + 0x48].copy_from_slice(&mask.to_le_bytes());
                    }
                }
            }
        }
        if q.width == 0 || q.height == 0 {
            continue;
        }
        let Some(h) = off(u32at(r, d + 0x34).wrapping_add(4), 0x8c) else {
            continue;
        };
        let Some(def) = off(u32at(r, h + 0x14), 28) else {
            continue;
        };
        let target = record_base(r) + slot * 52 + 16;
        if def != target {
            b.proxies[slot].original_definition = BASE + def as u32;
        }
        let mut data = [0u8; 28];
        data.copy_from_slice(&r[def..def + 28]);
        for (i, v) in [
            0i16,
            0,
            -(q.width as i16 / 2),
            17 - q.height as i16,
            q.width as i16,
            q.height as i16,
        ]
        .iter()
        .enumerate()
        {
            data[16 + i * 2..18 + i * 2].copy_from_slice(&v.to_le_bytes());
        }
        r[target..target + 28].copy_from_slice(&data);
        put32(r, h + 0x14, BASE + target as u32);
    }
}
/// Original Kirby current HP (damage, guard and source invulnerability remain native).
#[no_mangle]
pub unsafe extern "C" fn kssu_health(handle: *mut std::ffi::c_void) -> i32 {
    if handle.is_null() {
        return -1;
    }
    let r = (&*(handle as *mut Bridge)).nds.main_ram();
    player_health_group(r)
        .map(|g| (u16at(r, g + 0x23c) as i16 as i32).max(0))
        .unwrap_or(-2)
}

/// Queue a host attack through an ORIGINAL enemy contact collision. Direction
/// indicates which side the attacker occupies (-1 left, +1 right). Original
/// guard, recoil, invulnerability and ability-drop logic decide the result.
#[no_mangle]
pub unsafe extern "C" fn kssu_hurt(
    handle: *mut std::ffi::c_void,
    damage: u16,
    direction: i32,
) -> i32 {
    if handle.is_null() || damage == 0 || damage > 32767 || direction == 0 {
        return -1;
    }
    let b = &mut *(handle as *mut Bridge);
    stale(b);
    b.proxies[0].width = damage;
    b.proxies[0].height = if direction < 0 { 0 } else { 1 };
    0
}
fn prepare_hurt_proxy(b: &mut Bridge) {
    let frame = b.nds.current_frame();
    let r = b.nds.main_ram_mut();
    let player = u32at(r, 0x49df0);
    let Some(hero) = off(player, 0x9c) else {
        return;
    };
    let x = u32at(r, hero + 0x80) as i32;
    let y = u32at(r, hero + 0x84) as i32;
    let Some(p) = proxy_nodes(r)[0] else {
        let record = record_base(r);
        r[record..record + 52].fill(0xff);
        r[record..record + 5].copy_from_slice(&[1, 1, 0, 0, 2]);
        r[record + 8] = 0;
        r[record + 10..record + 12].copy_from_slice(&(((x >> 16) + 100) as i16).to_le_bytes());
        r[record + 12..record + 14].copy_from_slice(&((y >> 16) as i16).to_le_bytes());
        b.proxies[0].flags = 16;
        b.proxies[0].hp = 32767;
        return;
    };
    if b.proxies[0].flags == 0 || frame <= b.proxies[0].born_frame {
        return;
    }
    let Some(d) = off(u32at(r, p + 0x40), 0x90) else {
        return;
    };
    let Some(h) = off(u32at(r, d + 0x34).wrapping_add(4), 0x8c) else {
        return;
    };
    r[d + 0x10..d + 0x12].copy_from_slice(&32767i16.to_le_bytes());
    for field in [0x34, 0x38] {
        if let Some(g) = off(u32at(r, d + field), 0x240) {
            for at in [0x23a, 0x23c] {
                r[g + at..g + at + 2].copy_from_slice(&32767i16.to_le_bytes());
            }
        }
    }
    r[p + 0x88..p + 0x94].fill(0);
    let damage = b.proxies[0].width;
    if damage != 0 {
        let Some(def) = off(u32at(r, h + 0x14), 28) else {
            return;
        };
        let mut data = [0u8; 28];
        data.copy_from_slice(&r[def..def + 28]);
        data[4..6].copy_from_slice(&(-(damage as i16)).to_le_bytes());
        let target = record_base(r) + 16;
        r[target..target + 28].copy_from_slice(&data);
        put32(r, h + 0x14, BASE + target as u32);
        let source_x = x + if b.proxies[0].height == 0 {
            -8 * 65536
        } else {
            8 * 65536
        };
        for at in [0x78, 0x80] {
            put32(r, p + at, source_x as u32);
        }
        for at in [0x7c, 0x84] {
            put32(r, p + at, y as u32);
        }
        put32(r, p + 0x30, 0x0219806c);
        put32(r, p + 0x2c, 0x0214dbcc);
        put32(r, p + 0x3c, 0x0214dd74);
        b.proxies[0].width = 0;
    } else {
        // Keep normal AI/physics/hit-graph maintenance. Only the source
        // off-screen despawn callback is replaced with an original no-op.
        put32(r, p + 0x30, 0x0219806c);
        put32(r, p + 0x2c, 0x0214dbcc);
        put32(r, p + 0x3c, 0x02154138);
        for at in [0x78, 0x80, 0x7c, 0x84] {
            put32(r, p + at, (-4096i32 * 65536) as u32);
        }
        for field in [0x34, 0x38] {
            if let Some(g) = off(u32at(r, d + field), 0x240) {
                for index in 0..4 {
                    let n = g + 4 + index * 0x8c;
                    for at in (0x1c..0x2c).step_by(2) {
                        r[n + at..n + at + 2].copy_from_slice(&(-4096i16).to_le_bytes());
                    }
                }
            }
        }
    }
}

fn player_health_group(r: &[u8]) -> Option<usize> {
    let player = u32at(r, 0x49df0);
    // Invulnerable actions such as Cook unlink the active hurt node, but the
    // player's persistent hit-group allocation remains authoritative for HP.
    if let Some(p) = off(player, 0x9c) {
        if let Some(data) = off(u32at(r, p + 0x40), 0x28c) {
            if let Some(group) = off(u32at(r, data + 0x288), 0x240) {
                if u32at(r, group + 0x3c) == player && u16at(r, group + 0x23a) > 0 {
                    return Some(group);
                }
            }
        }
    }
    let mut a = u32at(r, 0x76f00 + 14 * 4);
    let mut seen = std::collections::HashSet::new();
    while let Some(h) = off(a, 0x8c) {
        if !seen.insert(a) || seen.len() > 256 {
            break;
        }
        if u32at(r, h + 0x38) == player {
            return off(u32at(r, h + 0x30), 0x240);
        }
        a = u32at(r, h);
    }
    None
}
#[no_mangle]
pub unsafe extern "C" fn kssu_max_health(handle: *mut std::ffi::c_void) -> i32 {
    if handle.is_null() {
        return -1;
    }
    let r = (&*(handle as *mut Bridge)).nds.main_ram();
    player_health_group(r)
        .map(|g| u16at(r, g + 0x23a) as i16 as i32)
        .unwrap_or(-2)
}
/// Synchronize host health without changing native hurt/action/guard timers.
#[no_mangle]
pub unsafe extern "C" fn kssu_set_health(
    handle: *mut std::ffi::c_void,
    current: u16,
    maximum: u16,
) -> i32 {
    if handle.is_null() || maximum == 0 || maximum > 32767 || current > maximum {
        return -1;
    }
    let b = &mut *(handle as *mut Bridge);
    stale(b);
    let r = b.nds.main_ram_mut();
    let Some(g) = player_health_group(r) else {
        return -2;
    };
    r[g + 0x23a..g + 0x23c].copy_from_slice(&maximum.to_le_bytes());
    r[g + 0x23c..g + 0x23e].copy_from_slice(&current.to_le_bytes());
    r[g + 0x23e..g + 0x240].copy_from_slice(&maximum.to_le_bytes());
    0
}

fn set_entity_health(r: &mut [u8], p: usize, hp: i32) {
    let Some(d) = off(u32at(r, p + 0x40), 0x90) else {
        return;
    };
    r[d + 0x10..d + 0x12].copy_from_slice(&(hp.max(1) as i16).to_le_bytes());
    for field in [0x34, 0x38] {
        if let Some(h) = off(u32at(r, d + field), 0x240) {
            r[h + 0x23a..h + 0x23c].copy_from_slice(&32767i16.to_le_bytes());
            r[h + 0x23c..h + 0x23e].copy_from_slice(&(hp.max(1) as i16).to_le_bytes());
        }
    }
}

/// Exact original engine-A scanout layers: RGB6 top/under, blend control,
/// composite RGB6, and the scanline's standard background palette.
#[no_mangle]
pub unsafe extern "C" fn kssu_read_layers(
    handle: *mut std::ffi::c_void,
    dest: *mut u32,
    count: usize,
    plane: u32,
) -> i32 {
    if handle.is_null() || dest.is_null() || count != 256 * 192 || plane > 4 {
        return -1;
    }
    let b = &mut *(handle as *mut Bridge);
    if b.nds
        .layer_capture(plane, std::slice::from_raw_parts_mut(dest, count))
    {
        0
    } else {
        -2
    }
}

/// Assign an enemy's source copy result before ingestion. Original multi-inhale
/// aggregation and Mix remain native. Bosses reject source grab/Cook/inhale
/// collision categories while retaining damage categories and host anchoring.
#[no_mangle]
pub unsafe extern "C" fn kssu_proxy_options(
    handle: *mut std::ffi::c_void,
    slot: u32,
    ability: u8,
    boss: bool,
) -> i32 {
    if handle.is_null() || !(1..PROXY_COUNT as u32).contains(&slot) || ability > 25 {
        return -1;
    }
    let b = &mut *(handle as *mut Bridge);
    b.proxies[slot as usize].options =
        (b.proxies[slot as usize].options & !0x1ff) | ability as u32 | if boss { 0x100 } else { 0 };
    0
}

fn basic_base(r: &[u8]) -> usize {
    (u32at(r, 0x9f390) - BASE) as usize
}
fn record_base(r: &[u8]) -> usize {
    (u32at(r, 0x9f394) - BASE) as usize
}
// Reserve one aligned block using the original NitroSystem ExpHeap layout.
// This narrow first-fit operation validates both complete linked lists before
// mutation, then splits a free block and appends its used header exactly as the
// original allocator (0x0201911c/0x0201929c) does for alignment4.
fn scene_alloc(r: &mut [u8], size: usize) -> Option<usize> {
    let heap = 0x1e5360;
    if u32at(r, heap) != 0x45585048 || size & 3 != 0 {
        return None;
    }
    let start = u32at(r, heap + 0x18);
    let end = u32at(r, heap + 0x1c);
    let mut candidate = None;
    for (head, tag) in [(0x24, 0x4652u16), (0x2c, 0x5544u16)] {
        let mut address = u32at(r, heap + head);
        let mut previous = 0;
        let mut seen = std::collections::HashSet::new();
        while address != 0 {
            let p = off(address, 16)?;
            if address < start
                || address >= end
                || address & 3 != 0
                || !seen.insert(address)
                || seen.len() > 4096
                || u16at(r, p) != tag
                || u32at(r, p + 8) != previous
            {
                return None;
            }
            let bytes = u32at(r, p + 4) as usize;
            if address.checked_add((16 + bytes) as u32)? > end {
                return None;
            }
            if tag == 0x4652 && bytes >= size + 36 && candidate.is_none() {
                candidate = Some(p);
            }
            previous = address;
            address = u32at(r, p + 12);
        }
        if previous != u32at(r, heap + head + 4) {
            return None;
        }
    }
    let p = candidate?;
    let old_size = u32at(r, p + 4) as usize;
    let prev = u32at(r, p + 8);
    let next = u32at(r, p + 12);
    let remaining = p + 16 + size;
    let remaining_address = BASE + remaining as u32;
    put32(r, remaining, 0x4652);
    put32(r, remaining + 4, (old_size - size - 16) as u32);
    put32(r, remaining + 8, prev);
    put32(r, remaining + 12, next);
    if prev == 0 {
        put32(r, heap + 0x24, remaining_address);
    } else {
        put32(r, off(prev, 16)? + 12, remaining_address);
    }
    if next == 0 {
        put32(r, heap + 0x28, remaining_address);
    } else {
        put32(r, off(next, 16)? + 8, remaining_address);
    }
    let tail = u32at(r, heap + 0x30);
    let group = u16at(r, heap + 0x34) & 255;
    put32(r, p, 0x5544 | ((group as u32) << 16));
    put32(r, p + 4, size as u32);
    put32(r, p + 8, tail);
    put32(r, p + 12, 0);
    if tail == 0 {
        put32(r, heap + 0x2c, BASE + p as u32);
    } else {
        put32(r, off(tail, 16)? + 12, BASE + p as u32);
    }
    put32(r, heap + 0x30, BASE + p as u32);
    Some(p + 16)
}
fn expand_scene(r: &mut [u8]) -> bool {
    if r[0x9f3e8] as usize == PROXY_COUNT {
        let Some(b) = off(u32at(r, 0x9f390), PROXY_COUNT * 8) else {
            return false;
        };
        let Some(p) = off(u32at(r, 0x9f394), PROXY_COUNT * 52) else {
            return false;
        };
        return b >= 16 && &r[b - 16..b - 8] == b"KSSUPRX4" && p == b + PROXY_COUNT * 8;
    }
    if r[0x9f3e8] != 8 || u32at(r, 0x9f390) != 0x0226d548 || u32at(r, 0x9f394) != 0x0226d5c0 {
        return false;
    }
    let original_basic = r[0x26d548..0x26d548 + 8 * 8].to_vec();
    let original_records = r[0x26d5c0..0x26d5c0 + 8 * 52].to_vec();
    let Some(p) = scene_alloc(r, 16 + PROXY_COUNT * 60) else {
        return false;
    };
    let basic = p + 16;
    let records = basic + PROXY_COUNT * 8;
    r[p..p + 16].fill(0);
    r[p..p + 8].copy_from_slice(b"KSSUPRX4");
    put32(r, p + 8, PROXY_COUNT as u32);
    r[basic..records].fill(0);
    r[basic..basic + 8 * 8].copy_from_slice(&original_basic);
    r[records..records + PROXY_COUNT * 52].fill(0xff);
    r[records..records + 8 * 52].copy_from_slice(&original_records);
    for i in 8..PROXY_COUNT {
        r[records + i * 52] = 0;
    }
    for (node, _) in descendants(r, u32at(r, 0x49df0)) {
        if let Some(slot) = proxy_slot_for(r, node) {
            if let Some(d) = off(u32at(r, node + 0x40), 0x90) {
                put32(r, d + 0x8c, BASE + (records + slot * 52) as u32);
            }
        }
    }
    put32(r, 0x9f390, BASE + basic as u32);
    put32(r, 0x9f394, BASE + records as u32);
    r[0x9f3e8] = PROXY_COUNT as u8;
    true
}

// The original's collision groups (0x244 bytes: four hit-box sets, then the
// class, copy and health at +0x238) come from one pool that overlay 23 sets up
// at a stage load (0x02071C00): 48 groups at 0x02077428, linked into a free
// list at 0x02076F84 and taken and given back only through it and the used list
// at 0x02076F80 (0x020714DC for the creator 0x020742C8, 0x02071508). An enemy
// holds one for its body and one more from the start of each hit recoil
// (0x02156ACC) for about seven frames, and the creator returns null once the
// free list is empty. The recoil routine writes through that result unchecked,
// so one attack that hit 24 or more proxies emptied the pool and the ARM9 took a
// data abort that stopped the original for good (#151). The original's rooms
// hold a few enemies; the bridge's hold up to 31 proxies. Two more groups for
// each proxy slot come from the scene heap and are linked after the original's
// free groups, which are still taken first. The count at +0x8C, which only the
// stage-load setup reads, right after writing 48, records that the pool holds
// them; after another stage load they are linked again. No code changes.
const HIT_POOL: usize = 0x76f00;
const HIT_GROUP_SIZE: usize = 0x244;
const HIT_GROUPS: u16 = 48;
const HIT_GROUPS_ADDED: usize = 2 * PROXY_COUNT;
const HIT_POOL_MAGIC: &[u8; 8] = b"KSSUHIT1";
fn extend_hit_pool(r: &mut [u8]) -> bool {
    let total = HIT_GROUPS + HIT_GROUPS_ADDED as u16;
    let count = u16at(r, HIT_POOL + 0x8c);
    if count == total {
        return true;
    }
    if count != HIT_GROUPS || u32at(r, HIT_POOL + 0x90) != 0x02077428 {
        return false;
    }
    let size = 16 + HIT_GROUPS_ADDED * HIT_GROUP_SIZE;
    let block = match scene_block(r, HIT_POOL_MAGIC, size) {
        Some(p) => p,
        None => {
            let Some(p) = scene_alloc(r, size) else {
                return false;
            };
            r[p..p + 16].fill(0);
            r[p..p + 8].copy_from_slice(HIT_POOL_MAGIC);
            put32(r, p + 8, HIT_GROUPS_ADDED as u32);
            p
        }
    };
    // The free list's last link, bounded like the original's own 48.
    let mut link = HIT_POOL + 0x84;
    for _ in 0..=HIT_GROUPS {
        let next = u32at(r, link);
        if next == 0 {
            break;
        }
        let Some(p) = off(next, HIT_GROUP_SIZE) else {
            return false;
        };
        link = p;
    }
    if u32at(r, link) != 0 {
        return false;
    }
    for i in 0..HIT_GROUPS_ADDED {
        let group = block + 16 + i * HIT_GROUP_SIZE;
        r[group..group + HIT_GROUP_SIZE].fill(0);
        put32(r, link, BASE + group as u32);
        link = group;
    }
    r[HIT_POOL + 0x8c..HIT_POOL + 0x8e].copy_from_slice(&total.to_le_bytes());
    true
}
// A block this bridge allocated earlier in the scene heap, found by the magic at
// the start of its data, or None.
fn scene_block(r: &[u8], magic: &[u8; 8], size: usize) -> Option<usize> {
    let heap = 0x1e5360;
    if u32at(r, heap) != 0x45585048 {
        return None;
    }
    let mut address = u32at(r, heap + 0x2c);
    for _ in 0..4096 {
        let p = off(address, 16)?;
        if u16at(r, p) != 0x5544 {
            return None;
        }
        if u32at(r, p + 4) as usize >= size && off(address, 16 + size).is_some() && &r[p + 16..p + 24] == magic {
            return Some(p + 16);
        }
        address = u32at(r, p + 12);
    }
    None
}
#[no_mangle]
pub extern "C" fn kssu_proxy_capacity() -> u32 {
    (PROXY_COUNT - 1) as u32
}

/// Permit source body-contact damage, or exclude only that original collision class.
#[no_mangle]
pub unsafe extern "C" fn kssu_proxy_contact(
    handle: *mut std::ffi::c_void,
    slot: u32,
    enabled: bool,
) -> i32 {
    if handle.is_null() || !(1..PROXY_COUNT as u32).contains(&slot) {
        return -1;
    }
    let b = &mut *(handle as *mut Bridge);
    let was_disabled = b.proxies[slot as usize].options & 0x200 != 0;
    if enabled {
        b.proxies[slot as usize].options &= !0x200;
        if was_disabled {
            let r = b.nds.main_ram_mut();
            if let Some(p) = proxy_nodes(r)[slot as usize] {
                if let Some(d) = off(u32at(r, p + 0x40), 0x90) {
                    for field in [0x34, 0x38] {
                        if let Some(group) = off(u32at(r, d + field), 0x240) {
                            for index in 0..4 {
                                let node = group + 4 + index * 0x8c;
                                let mask = u16at(r, node + 0x46) & !0x4000;
                                r[node + 0x46..node + 0x48].copy_from_slice(&mask.to_le_bytes());
                            }
                        }
                    }
                }
            }
        }
    } else {
        b.proxies[slot as usize].options |= 0x200;
    }
    0
}
/// Relinquish host geometry and copy overrides without destroying the original corpse/ice block.
#[no_mangle]
pub unsafe extern "C" fn kssu_proxy_detach(handle: *mut std::ffi::c_void, slot: u32) -> i32 {
    if handle.is_null() || !(1..PROXY_COUNT as u32).contains(&slot) {
        return -1;
    }
    let b = &mut *(handle as *mut Bridge);
    stale(b);
    let q = &mut b.proxies[slot as usize];
    let r = b.nds.main_ram_mut();
    if off(q.original_definition, 28).is_some() {
        if let Some(p) = proxy_nodes(r)[slot as usize] {
            if let Some(d) = off(u32at(r, p + 0x40), 0x90) {
                if let Some(node) = off(u32at(r, d + 0x34).wrapping_add(4), 0x8c) {
                    let replacement = BASE + (record_base(r) + slot as usize * 52 + 16) as u32;
                    if u32at(r, node + 0x14) == replacement {
                        put32(r, node + 0x14, q.original_definition);
                    }
                }
            }
        }
    }
    q.width = 0;
    q.height = 0;
    q.options = 0;
    q.original_definition = 0;
    0
}

/// Retire completed original food drops at a host room boundary. Bound enemies
/// and frozen blocks are retired through proxy_set(false) by the host separately.
/// Restrict this to the validated free-food update; held/captured entities stay
/// with their original controller, as do Kirby, helpers, caps and projectiles.
#[no_mangle]
pub unsafe extern "C" fn kssu_room_cleanup(handle: *mut std::ffi::c_void) -> i32 {
    if handle.is_null() {
        return -1;
    }
    let b = &mut *(handle as *mut Bridge);
    stale(b);
    let r = b.nds.main_ram_mut();
    let nodes = descendants(r, u32at(r, 0x49df0));
    let mut retiring = std::collections::HashSet::new();
    for &(p, _) in &nodes {
        if r[p + 0x16] == 104 && u32at(r, p + 4) == 0 && u32at(r, p + 0x30) == 0x021715f8 {
            retiring.insert(BASE + p as u32);
        }
    }
    loop {
        let previous = retiring.len();
        for &(p, _) in &nodes {
            if retiring.contains(&u32at(r, p + 4)) {
                retiring.insert(BASE + p as u32);
            }
        }
        if previous == retiring.len() {
            break;
        }
    }
    for p in &retiring {
        let p = (*p - BASE) as usize;
        r[p + 0x14] |= 0x10;
        put32(r, p + 0x30, 0x0200e668);
    }
    retiring.len() as i32
}

// Waddle Dee proxies run their original physics and action VM. Its walking
// script has a separate ambient footstep every31 ticks, even when parked off
// screen. Host enemies supply their own walking audio. Suppress only these six
// validated Waddle VM calls; Kirby/helper movement and original ability SFX use
// other callers and remain untouched. The user's ROM file is never changed.
fn mute_proxy_footsteps(r: &mut [u8]) -> bool {
    const CALLS: [u32; 6] = [
        0x021999b0, 0x02199a2c, 0x02199aac, 0x02199b28, 0x02199ba8, 0x02199c24,
    ];
    const NOP: u32 = 0xe1a00000;
    for address in CALLS {
        let expected =
            0xeb000000 | (((0x0200cd9ci32 - address as i32 - 8) >> 2) as u32 & 0x00ffffff);
        let instruction = u32at(r, (address - BASE) as usize);
        if instruction != expected && instruction != NOP {
            return false;
        }
    }
    for address in CALLS {
        code32(r, (address - BASE) as usize, NOP);
    }
    true
}

// An original Waddle Dee that enters water is defeated: when the in-water byte
// (d+0x50)+7 becomes1, the source calls the enter-water handler at (d+0x54)+8,
// and Waddle Dee's reaction table at0x0219C7C8 holds the default handler
// 0x02159440, which calls the defeat entry0x02158FC0. During hit recoil the
// shared recoil table's handler0x02156540 calls the saved table's handler, or
// that same defeat when it is empty, so the entry must hold a real handler.
// Proxies follow host enemies, which do not die in water: the dying proxy
// reported source control, pinned its host enemy, ignored inhale and attacks,
// and was rebound in the same water after its destruction; the parked damage
// carrier died the same way when Kirby swam. Point only this validated entry at
// the original return-zero stub0x020CF36C (`mov r0,#0; bx lr`), so the water
// event is not handled. Kirby's swimming, the exit-water handler and every
// other enemy's water reaction are untouched. The user's ROM file is never changed.
fn keep_proxies_alive_in_water(r: &mut [u8]) -> bool {
    const ENTER_WATER: u32 = 0x0219c7c8 + 8;
    const DEFEAT: u32 = 0x02159440;
    const IGNORE: u32 = 0x020cf36c;
    let stub = (IGNORE - BASE) as usize;
    if u32at(r, stub) != 0xe3a00000 || u32at(r, stub + 4) != 0xe12fff1e {
        return false;
    }
    let p = (ENTER_WATER - BASE) as usize;
    let handler = u32at(r, p);
    if handler != DEFEAT && handler != IGNORE {
        return false;
    }
    code32(r, p, IGNORE);
    true
}

// The original CPU helper's AI notices a target and then waits before it
// approaches: the wait getter 0x020B53C0 returns the global AI block's reaction
// time (0x020BC038+0x4C, 60 frames by default; the target routine sets it from
// the distance class and helper type, up to 120 frames and more). Both callers
// of the notice state take the smaller of the stored timer and this value
// every frame, so an enemy was first hit two to three seconds after it
// appeared. The getter's two halfword sign-extension instructions (the value
// is already sign-extended by its load) become `cmp r0,#6; movgt r0,#6`, which
// caps that wait at six frames. Nothing else of the helper's AI, its attacks or
// targeting changes. The user's ROM file is never changed.
const HELPER_REACTION_CAP: u32 = 6;
fn quicken_helper_reaction(r: &mut [u8]) -> bool {
    const GETTER: usize = 0x020b53c0 - BASE as usize;
    const PREFIX: [u32; 5] = [0xe59f0018, 0xe5901044, 0xe3510000, 0x11d004fc, 0x03a0003c];
    const SIGN_EXTEND: [u32; 2] = [0xe1a00800, 0xe1a00840];
    const TAIL: [u32; 2] = [0xe12fff1e, 0x020bc038];
    let patched = [
        0xe3500000 | HELPER_REACTION_CAP,
        0xc3a00000 | HELPER_REACTION_CAP,
    ];
    let words: Vec<u32> = (0..9).map(|i| u32at(r, GETTER + i * 4)).collect();
    if words[..5] != PREFIX || words[7..] != TAIL {
        return false;
    }
    if words[5..7] != SIGN_EXTEND && words[5..7] != patched {
        return false;
    }
    code32(r, GETTER + 20, patched[0]);
    code32(r, GETTER + 24, patched[1]);
    true
}

// A mouthful of two or more objects runs the original Mix roulette. Its wheel
// is the 25-byte table at 0x020BAA8C: the setup 0x020AF140 derives the start
// and auto-stop positions from the two swallowed copies, and 0x020AF1C8 reads
// the face shown, the stop and the result from it. Three faces are limited-use
// abilities (Mike, Crash and Cook; Paint is not on the wheel), which Kirby
// never gets from a swallow (#102): a stop there made one his main copy
// (#141). Each of those faces shows the next face instead, so the wheel keeps
// its original order, speed and length and those three last twice as long.
// The roulette code and its other faces are untouched. The user's ROM file is
// never changed.
fn keep_limited_abilities_off_mix_wheel(r: &mut [u8]) -> bool {
    const WHEEL: usize = 0x020baa8c - BASE as usize;
    const FACES: [u8; 25] = [
        11, 2, 22, 7, 13, 18, 15, 8, 16, 36, 4, 6, 25, 23, 14, 12, 20, 10, 3, 19, 24, 5, 17, 32, 9,
    ];
    const LIMITED: std::ops::RangeInclusive<u8> = 21..=24;
    for (i, &face) in FACES.iter().enumerate() {
        let allowed = if LIMITED.contains(&face) { FACES[i + 1] } else { face };
        if r[WHEEL + i] != face && r[WHEEL + i] != allowed {
            return false;
        }
    }
    for (i, &face) in FACES.iter().enumerate() {
        if LIMITED.contains(&face) {
            r[WHEEL + i] = FACES[i + 1];
        }
    }
    true
}

// The original OAM builder0x020100A0 turns the main screen's sprite records
// (manager0x020598E0: sixteen priority lists of 0x1C-byte records, each with a
// list of 8-byte pieces) into the shadow OAM at *manager, and drops every piece
// outside its own screen (x <= -64 or > 256, y <= -64 or > 192). The host view
// is wider than the DS screen, so shots more than about 128 px from Kirby kept
// flying and hitting without being drawn. After each frame the bridge runs the
// same assembly over the same records. It accepts the result only when its
// on-screen entries equal the shadow OAM the ROM just wrote, and keeps the
// pieces the ROM culled, with full positions. They are reported once the
// hardware OAM equals that shadow OAM (one frame later), so they pair with the
// OAM, VRAM and palettes the host reads. Nothing in the machine is changed.
const OBJ_MANAGER: u32 = 0x020598e0;
const FAR_LIMIT: usize = 256;
fn build_far_objects(r: &[u8]) -> Option<(Vec<FarObj>, Vec<u8>)> {
    let m = off(OBJ_MANAGER, 0x98)?;
    let shadow = off(u32at(r, m), 1024)?;
    let base_attr = u16at(r, m + 0x12);
    let mut far = Vec::new();
    let mut matrices: Vec<[u16; 4]> = Vec::new();
    let mut count = 0usize;
    'lists: for list in 0..16 {
        let mut record = u32at(r, m + 0x18 + list * 4);
        let mut records = 0;
        while record != 0 {
            records += 1;
            let l = off(record, 0x1c)?;
            if records > 256 {
                return None;
            }
            let flags = u16at(r, l + 4);
            let attr = u16at(r, l + 6);
            let (rx, ry) = (u16at(r, l + 8) as i16 as i32, u16at(r, l + 10) as i16 as i32);
            let affine = flags & 0x100 != 0;
            // Affine records share matrices by value, at most 32; a record
            // that finds no free matrix is skipped whole.
            let mut matrix = 0u16;
            let mut skip = false;
            if affine {
                let value = [0x14, 0x16, 0x18, 0x1a].map(|at| u16at(r, l + at));
                if let Some(i) = matrices.iter().position(|&v| v == value) {
                    matrix = i as u16;
                } else if matrices.len() < 32 {
                    matrices.push(value);
                    matrix = matrices.len() as u16 - 1;
                } else {
                    skip = true;
                }
            }
            let mut piece = u32at(r, l + 0x10);
            for pieces in 0.. {
                if skip {
                    break;
                }
                let p = off(piece, 8)?;
                if pieces > 128 {
                    return None;
                }
                let w0 = u16at(r, p);
                // A hidden (non-affine) record keeps no pieces.
                if flags & 0x200 == 0 || affine {
                    let y = ry + (w0 as u8 as i8) as i32;
                    let w1 = u16at(r, p + if flags & 0x8000 != 0 { 4 } else { 2 });
                    let x = rx + (((w1 << 7) as i16) >> 7) as i32;
                    let w3 = u16at(r, p + 6);
                    let attr0 = (y as u16 & 0xff) | (w0 & 0xe000) | (flags & 0x1f00);
                    let mut attr1 = (x as u16 & 0x1ff) | (w1 & 0xf000);
                    if affine {
                        attr1 = (attr1 & !0x3e00) | (matrix << 9);
                    }
                    let palette = if w0 & 0x800 != 0 {
                        w3 >> 12
                    } else if flags & 4 != 0 {
                        attr >> 12
                    } else {
                        ((w3 >> 12) + (attr >> 12)) & 15
                    };
                    let level = |v: u16| (v >> 10) & 3;
                    let priority = match (flags & 0x4000 != 0, flags & 2 != 0) {
                        (true, true) => level(attr),
                        (true, false) => level(w3) + level(attr),
                        (false, true) => level(base_attr) + level(attr),
                        (false, false) => level(attr) + level(base_attr) + level(w3),
                    } & 3;
                    let tile = if w0 & 0x400 != 0 {
                        w3 & 1023
                    } else if flags & 1 != 0 {
                        attr & 1023
                    } else {
                        ((w3 & 1023) + (attr & 1023)) & 1023
                    };
                    let attr2 = tile | (priority << 10) | (palette << 12);
                    if x > -64 && x <= 256 && y > -64 && y <= 192 {
                        // attr1 bits 9-11 of a non-affine piece are left over in the ROM.
                        let q = shadow + count * 8;
                        let keep = if affine { 0xffff } else { 0xf1ff };
                        if u16at(r, q) != attr0 || u16at(r, q + 2) & keep != attr1 & keep || u16at(r, q + 4) != attr2 {
                            return None;
                        }
                        count += 1;
                        if count == 128 {
                            break 'lists;
                        }
                    } else if far.len() < FAR_LIMIT {
                        far.push(FarObj {
                            x: x.clamp(-32768, 32767) as i16,
                            y: y.clamp(-32768, 32767) as i16,
                            attr0,
                            attr1,
                            attr2,
                            order: count as u16,
                        });
                    }
                }
                if w0 & 0x1000 != 0 {
                    break;
                }
                piece = piece.wrapping_add(8);
            }
            record = u32at(r, l);
        }
    }
    // The ROM hides the unused entries with y 0xF8 (attr1 0).
    if (count..128).any(|k| u16at(r, shadow + k * 8) != 0xf8 || u16at(r, shadow + k * 8 + 2) != 0) {
        return None;
    }
    Some((far, r[shadow..shadow + 1024].to_vec()))
}
/// The original empties its sprite lists as a logic pass begins and fills them
/// near its end, just before it builds the shadow OAM from them. Empty lists
/// beside a shadow OAM that still has entries mean a pass is under way: the
/// actors and the camera may already be updated, the sprites are not built.
fn pass_unfinished(r: &[u8]) -> bool {
    let Some(m) = off(OBJ_MANAGER, 0x98) else {
        return false;
    };
    let Some(shadow) = off(u32at(r, m), 1024) else {
        return false;
    };
    (0..16).all(|list| u32at(r, m + 0x18 + list * 4) == 0)
        && (0..128).any(|k| u16at(r, shadow + k * 8) != 0xf8 || u16at(r, shadow + k * 8 + 2) != 0)
}
/// Source frames since `kssu_open` that ended inside an original logic pass.
/// The step ends at VBlank so that none does (see `kssu_step`); it counts up
/// only when a pass takes longer than a whole frame, and such a frame may draw
/// Kirby a terrain cell off if the window moves under it (#134).
#[no_mangle]
pub unsafe extern "C" fn kssu_unfinished_passes(handle: *mut std::ffi::c_void) -> i32 {
    if handle.is_null() {
        return -1;
    }
    (&*(handle as *mut Bridge)).unfinished_passes.min(i32::MAX as u32) as i32
}
fn update_far_objects(b: &mut Bridge) {
    // Engine A's OAM as one block (#165); the copy refuses only a range outside it.
    let mut oam = [0u8; 1024];
    let read = b.nds.gpu_memory(OAM, &mut oam);
    b.far_shown = match b.far_pending.take() {
        Some((far, shadow)) if read && shadow == oam => far,
        _ => Vec::new(),
    };
    b.far_pending = build_far_objects(b.nds.main_ram_mut());
}
/// Copy the off-screen OBJ pieces of the OAM the hardware shows now: 12-byte
/// `FarObj` records in original OAM order. Returns the count copied.
#[no_mangle]
pub unsafe extern "C" fn kssu_far_objs(handle: *mut std::ffi::c_void, dest: *mut FarObj, capacity: usize) -> i32 {
    if handle.is_null() || (dest.is_null() && capacity != 0) {
        return -1;
    }
    let b = &*(handle as *mut Bridge);
    let count = b.far_shown.len().min(capacity);
    ptr::copy_nonoverlapping(b.far_shown.as_ptr(), dest, count);
    count as i32
}

// ---- Original ability change (#102) ----
// The F slot switches Kirby's copy through the ROM's own ability routine
// SetAbility(actor, record, ability) at 0x0211B734, the routine every original
// path uses (the swallow, each limited ability's last-use revert, the original
// give-ability routine 0x020AF020). The checked melonDS hook
// (tools/nds_call_patch.py) runs it once at the entry of the player's per-frame
// update, 0x020B2714, then resumes that update. It loads the ability's
// graphics from the cartridge, so it can take 2-5 frames to return.
extern "C" {
    static mut kssu_hook_at: u32;
    static mut kssu_hook_fn: u32;
    static mut kssu_hook_state: u32;
    static mut kssu_hook_args: [u32; 4];
    static mut kssu_hook_saved: [u32; 15];
    // The same patch's second stage (#153): nonzero runs the emulator's
    // recompiler instead of its interpreter, and the flush discards the
    // compiled blocks before the next ARM9 slice.
    static mut kssu_recompile: u32;
    static mut kssu_recompile_flush: u32;
}
const PLAYER_SLOT: usize = 0x49df0;
const PLAYER_RECORD: u32 = 0x020baf68;
const PLAYER_UPDATE: u32 = 0x020b2714;
const SET_ABILITY: u32 = 0x0211b734;
const ABILITY_MIKE: u8 = 22;
/// Mike's remaining shouts; its copy sets 3 and each shout takes one off.
const MIKE_SHOUTS: usize = 0x05b6b1;
const HOOK_MAGIC: &[u8; 8] = b"KSSUHK01";
const HOOK_WORDS: usize = 3 + 4 + 15;

/// Arm one original ability change. Returns 0 when armed, -2 while the original
/// would refuse it (the give-ability routine's own test: an active player standing,
/// walking, airborne or swimming, field 2 == 1 and action group 4, 6, 7 or 8;
/// an attack, a slide, a recoil or a ladder are other groups) or while Kirby holds
/// a mouthful, -3 while a change is still pending, and -4 when the player is not
/// the expected object. A mouthful keeps the groups of standing, walking and
/// jumping; after `SetAbility` the give-ability routine drops it (`0x020AEF70`
/// sets field 4 back to 1), which this call does not do, so it refuses instead
/// and the mouthful is spat or swallowed first (#158), on dry land and in water
/// (#166).
#[no_mangle]
pub unsafe extern "C" fn kssu_set_ability(handle: *mut std::ffi::c_void, ability: u8) -> i32 {
    if handle.is_null() || !(1..=25).contains(&ability) {
        return -1;
    }
    if matches!(kssu_hook_state, 1 | 2) {
        return -3;
    }
    let inhaling = kssu_action_flags(handle) & 1 != 0;
    let r = (&*(handle as *mut Bridge)).nds.main_ram();
    let actor = u32at(r, PLAYER_SLOT);
    let Some(a) = off(actor, 0x44) else {
        return -4;
    };
    if u32at(r, a + 0x30) != PLAYER_UPDATE || u32at(r, a + 0x40) != PLAYER_RECORD {
        return -4;
    }
    let record = (PLAYER_RECORD - BASE) as usize;
    if inhaling
        || r[record + 0x12] != 1
        || !matches!(r[record + 0x19], 4 | 6 | 7 | 8)
        || holds_mouthful(r[record + 0x14])
    {
        return -2;
    }
    kssu_hook_args = [actor, PLAYER_RECORD, ability as u32, 0];
    kssu_hook_fn = SET_ABILITY;
    kssu_hook_at = PLAYER_UPDATE;
    kssu_hook_state = 1;
    0
}
/// Player field 4 (record +0x14) while Kirby holds a mouthful, on dry land and in
/// water (#166).
const CARRY_MOUTHFUL: u8 = 2;
const CARRY_WATER_MOUTHFUL: u8 = 5;
fn holds_mouthful(carry: u8) -> bool {
    matches!(carry, CARRY_MOUTHFUL | CARRY_WATER_MOUTHFUL)
}
/// Kirby's move as the original keeps it (#158). The original's move selectors
/// (the stand's is `0x0210EA60`) and its give-ability routine read player field 4,
/// `carry`: 1 on dry land, 2 while Kirby holds a mouthful there, 4 in water and 5
/// with a mouthful in water (#166). A mouthful lasts from the stand after the catch
/// through a swallow until the copy loads, or a spit until the star leaves. The
/// mouth count behind `kssu_action_flags` bit 1 stays set after a swallow until the
/// next inhale; `carry` does not. `descriptor` is the running move (record +0x808),
/// a 24-byte descriptor, and `table` the copy's move table (+0x800, 0 for Normal):
/// `(descriptor - table) / 24` indexes a copy's own move, while standing, walking,
/// jumping and their mouthful and water variants come from tables every copy shares
/// (mouthful stand `0x02131508`, walk `0x02131148`, jump `0x021311C0`, fall
/// `0x02131238`, landing `0x021310E8`; in water the stand is `0x021314F0`, with a
/// mouthful `0x02131520`, and walking on the bottom is the wade, `0x02131160`, the
/// walk action under its own move).
#[repr(C)]
#[derive(Default, Copy, Clone)]
pub struct KssuMove {
    /// The action code, group then id (record +0x18, 0x020BAF80).
    pub action: u16,
    pub carry: u8,
    /// The copy byte, 1 for Normal.
    pub ability: u8,
    pub descriptor: u32,
    pub table: u32,
}
#[no_mangle]
pub unsafe extern "C" fn kssu_move(handle: *mut std::ffi::c_void, dest: *mut KssuMove) -> i32 {
    if handle.is_null() || dest.is_null() {
        return -1;
    }
    let r = (&*(handle as *mut Bridge)).nds.main_ram();
    let record = (PLAYER_RECORD - BASE) as usize;
    *dest = KssuMove {
        action: u16at(r, record + 0x18),
        carry: r[record + 0x14],
        ability: r[record + 0x13],
        descriptor: u32at(r, record + 0x808),
        table: u32at(r, record + 0x800),
    };
    0
}
/// 1 while an armed ability change has not returned yet, otherwise 0.
#[no_mangle]
pub unsafe extern "C" fn kssu_ability_change_pending(handle: *mut std::ffi::c_void) -> i32 {
    if handle.is_null() {
        return -1;
    }
    if kssu_hook_state == 3 {
        kssu_hook_state = 0;
    }
    matches!(kssu_hook_state, 1 | 2) as i32
}
/// Set Mike's remaining shouts (1-3) while Kirby holds Mike, before the shout.
#[no_mangle]
pub unsafe extern "C" fn kssu_set_mike_shouts(handle: *mut std::ffi::c_void, shouts: u8) -> i32 {
    if handle.is_null() || !(1..=3).contains(&shouts) {
        return -1;
    }
    let b = &mut *(handle as *mut Bridge);
    stale(b);
    let r = b.nds.main_ram_mut();
    if r[(PLAYER_RECORD - BASE) as usize + 0x13] != ABILITY_MIKE {
        return -2;
    }
    r[MIKE_SHOUTS] = shouts;
    0
}
// ---- Plasma's charge aura after an ability change (#135) ----
// Plasma's charge aura is an effect of class 9, type 20, sub-type 1 (update
// 0x020C838C). The original's charge-level routine 0x020ABFD4 creates it
// whenever it is missing (0x020FB914), whatever Kirby's copy is. SetAbility
// runs Plasma's exit handler, which removes the aura, but a Plasma pose handler
// (0x020FBCCC) still installed for that frame asks for the level once more, so
// the aura returns while another copy's graphics are loaded. Given back, Plasma
// creates a second aura, and the first draws invalid OBJ pieces on a charge or
// makes the OAM builder read invalid memory. The original only leaves Plasma
// once that pose has ended. The bridge removes a stray aura with the exit
// handler's own call, 0x020CC3B8(actor, type 20, sub-type 1, no filter), at the
// player's next update: any aura while Kirby is not Plasma, and all of them
// when Plasma has more than one (the level routine then creates one again).
const ABILITY_PLASMA: u8 = 12;
const PLASMA_AURA_UPDATE: u32 = 0x020c838c;
const PLASMA_AURA_TYPE: u8 = 20;
const REMOVE_EFFECTS: u32 = 0x020cc3b8;
/// The player actor when it carries a charge aura the original would not.
fn stray_plasma_aura(r: &[u8]) -> Option<u32> {
    let actor = u32at(r, PLAYER_SLOT);
    let a = off(actor, 0x44)?;
    if u32at(r, a + 0x30) != PLAYER_UPDATE || u32at(r, a + 0x40) != PLAYER_RECORD {
        return None;
    }
    let auras = actor_nodes(r)
        .into_iter()
        .filter(|&p| {
            u32at(r, p + 4) == actor
                && u32at(r, p + 0x30) == PLASMA_AURA_UPDATE
                && r[p + 0x44] == PLASMA_AURA_TYPE
                && r[p + 0x45] == 1
        })
        .count();
    let plasma = r[(PLAYER_RECORD - BASE) as usize + 0x13] == ABILITY_PLASMA;
    (auras > plasma as usize).then_some(actor)
}
unsafe fn remove_stray_plasma_aura(b: &Bridge) {
    if matches!(kssu_hook_state, 1 | 2) {
        return;
    }
    if let Some(actor) = stray_plasma_aura(b.nds.main_ram()) {
        kssu_hook_args = [actor, PLASMA_AURA_TYPE as u32, 1, 0];
        kssu_hook_fn = REMOVE_EFFECTS;
        kssu_hook_at = PLAYER_UPDATE;
        kssu_hook_state = 1;
    }
}
// The hook state belongs to the machine: a snapshot taken while a change loads
// must resume it, and any other snapshot clears it.
unsafe fn append_hook_trailer(data: &mut Vec<u8>) {
    let words = [kssu_hook_state, kssu_hook_at, kssu_hook_fn];
    for v in words.iter().chain(kssu_hook_args.iter()).chain(kssu_hook_saved.iter()) {
        data.extend(v.to_le_bytes());
    }
    data.extend(HOOK_MAGIC);
}
fn take_hook_trailer(data: &mut Vec<u8>) -> Option<[u32; HOOK_WORDS]> {
    let len = HOOK_WORDS * 4 + 8;
    if data.len() < len + 16 || &data[data.len() - 8..] != HOOK_MAGIC {
        return None;
    }
    let start = data.len() - len;
    let mut words = [0u32; HOOK_WORDS];
    for (i, w) in words.iter_mut().enumerate() {
        *w = u32at(data, start + i * 4);
    }
    data.truncate(start);
    Some(words)
}
unsafe fn restore_hook(words: Option<[u32; HOOK_WORDS]>) {
    let w = words.unwrap_or([0; HOOK_WORDS]);
    // A finished change (3) needs nothing more; only armed or running ones resume.
    kssu_hook_state = if matches!(w[0], 1 | 2) { w[0] } else { 0 };
    kssu_hook_at = w[1];
    kssu_hook_fn = w[2];
    kssu_hook_args.copy_from_slice(&w[3..7]);
    kssu_hook_saved.copy_from_slice(&w[7..]);
}

// Kept last so the Linux build, which leaves it out, keeps its line numbers.
#[cfg(windows)]
mod melon_windows;
