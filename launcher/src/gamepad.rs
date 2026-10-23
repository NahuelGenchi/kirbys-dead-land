//! Game controllers in the launcher (#261). A thread reads them, through XInput
//! on Windows and the kernel's event devices on Linux, and turns what it reads
//! into the window's keys: the D-pad or left stick moves the focus as Tab and
//! Shift+Tab do, A presses the focused button as Return does, and B closes the
//! overlay on top. While Dead Cells starts or runs, the controller is the game's.

use crate::{AppWindow, PlayState};
use slint::platform::{Key, WindowEvent};
use slint::{ComponentHandle, SharedString};
use std::time::{Duration, Instant};

/// How often the controllers are read.
const POLL: Duration = Duration::from_millis(16);
/// Reads between looks for controllers plugged in since: about two seconds.
const RESCAN: u32 = 125;
/// A held direction moves once, again after `FIRST_REPEAT`, then every `REPEAT`.
const FIRST_REPEAT: Duration = Duration::from_millis(450);
const REPEAT: Duration = Duration::from_millis(140);
/// How far the stick goes to count as a direction, and how far back to let go of it.
const STICK_ON: f32 = 0.5;
const STICK_OFF: f32 = 0.35;

/// The buttons and left stick of a controller, or of all of them together.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct Pad {
    pub up: bool,
    pub down: bool,
    pub left: bool,
    pub right: bool,
    pub a: bool,
    pub b: bool,
    /// The left stick, from -1 to 1 on each axis; `y` grows downwards.
    pub x: f32,
    pub y: f32,
}

impl Pad {
    /// Both controllers at once: a button pressed on either, the stick pushed further.
    fn merge(self, other: Pad) -> Pad {
        let further = |a: f32, b: f32| if b.abs() > a.abs() { b } else { a };
        Pad {
            up: self.up || other.up,
            down: self.down || other.down,
            left: self.left || other.left,
            right: self.right || other.right,
            a: self.a || other.a,
            b: self.b || other.b,
            x: further(self.x, other.x),
            y: further(self.y, other.y),
        }
    }
}

/// What the controller asks of the window.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Nav {
    /// Down or right: the next button, as Tab.
    Next,
    /// Up or left: the previous button, as Shift+Tab.
    Previous,
    /// A: press the focused button, as Return.
    Press,
    /// B: close the overlay on top.
    Back,
}

/// Turns controller readings into moves: a direction moves once and then
/// repeats while it is held; A and B act once for each press.
pub struct Navigator {
    /// The direction held, and when it moves again.
    held: Option<(Nav, Duration)>,
    a: bool,
    b: bool,
}

impl Navigator {
    /// A button already held when the launcher opens waits for its release.
    pub fn new() -> Navigator {
        Navigator { held: None, a: true, b: true }
    }

    fn direction(&self, pad: &Pad) -> Option<Nav> {
        // A stick already held keeps its direction until it comes back past STICK_OFF.
        let reach = |nav: Nav| if self.held.is_some_and(|(held, _)| held == nav) { STICK_OFF } else { STICK_ON };
        let next = pad.down || pad.right || pad.x > reach(Nav::Next) || pad.y > reach(Nav::Next);
        let previous = pad.up || pad.left || pad.x < -reach(Nav::Previous) || pad.y < -reach(Nav::Previous);
        match (next, previous) {
            (true, false) => Some(Nav::Next),
            (false, true) => Some(Nav::Previous),
            _ => None,
        }
    }

    /// The moves for one reading, taken `now` after the first.
    pub fn update(&mut self, pad: &Pad, now: Duration) -> Vec<Nav> {
        let mut moves = Vec::new();
        match (self.direction(pad), self.held) {
            (Some(nav), Some((held, at))) if nav == held => {
                if now >= at {
                    moves.push(nav);
                    self.held = Some((nav, now + REPEAT));
                }
            }
            (Some(nav), _) => {
                moves.push(nav);
                self.held = Some((nav, now + FIRST_REPEAT));
            }
            (None, _) => self.held = None,
        }
        if pad.a && !self.a {
            moves.push(Nav::Press);
        }
        if pad.b && !self.b {
            moves.push(Nav::Back);
        }
        (self.a, self.b) = (pad.a, pad.b);
        moves
    }
}

/// A raw axis reading between `min` and `max`, from -1 to 1.
fn axis(value: i32, min: i32, max: i32) -> f32 {
    if max <= min {
        return 0.0;
    }
    let middle = (min as f32 + max as f32) / 2.0;
    let half = (max as f32 - min as f32) / 2.0;
    ((value as f32 - middle) / half).clamp(-1.0, 1.0)
}

/// XInput, on Windows.
#[cfg(any(windows, test))]
mod xinput {
    use super::{axis, Pad};
    #[cfg(windows)]
    use windows_sys::Win32::UI::Input::XboxController::{XInputGetState, XINPUT_STATE};

    // The XINPUT_GAMEPAD_* button bits in XInput.h.
    const DPAD_UP: u16 = 0x0001;
    const DPAD_DOWN: u16 = 0x0002;
    const DPAD_LEFT: u16 = 0x0004;
    const DPAD_RIGHT: u16 = 0x0008;
    const A: u16 = 0x1000;
    const B: u16 = 0x2000;

    /// A controller as XInput reports it: `wButtons`, `sThumbLX` and `sThumbLY`
    /// of `XINPUT_GAMEPAD`, whose stick `y` grows upwards.
    pub fn from_xinput(buttons: u16, x: i16, y: i16) -> Pad {
        Pad {
            up: buttons & DPAD_UP != 0,
            down: buttons & DPAD_DOWN != 0,
            left: buttons & DPAD_LEFT != 0,
            right: buttons & DPAD_RIGHT != 0,
            a: buttons & A != 0,
            b: buttons & B != 0,
            x: axis(x.into(), i16::MIN.into(), i16::MAX.into()),
            y: -axis(y.into(), i16::MIN.into(), i16::MAX.into()),
        }
    }

    /// XInput's four controller slots.
    #[cfg(windows)]
    pub struct Pads {
        connected: [bool; 4],
        reads: u32,
    }

    #[cfg(windows)]
    impl Pads {
        pub fn new() -> Pads {
            Pads { connected: [false; 4], reads: 0 }
        }

        pub fn read(&mut self) -> Pad {
            // Asking an empty slot is slow, so empty slots are asked only now and then.
            let probe = self.reads % super::RESCAN == 0;
            self.reads = self.reads.wrapping_add(1);
            let mut pad = Pad::default();
            for (slot, connected) in self.connected.iter_mut().enumerate() {
                if !*connected && !probe {
                    continue;
                }
                let mut state = XINPUT_STATE::default();
                *connected = unsafe { XInputGetState(slot as u32, &mut state) } == 0;
                if *connected {
                    let gamepad = state.Gamepad;
                    pad = pad.merge(from_xinput(gamepad.wButtons, gamepad.sThumbLX, gamepad.sThumbLY));
                }
            }
            pad
        }
    }
}

/// The kernel's event devices, on Linux.
#[cfg(any(unix, test))]
mod evdev {
    use super::{axis, Pad};
    #[cfg(unix)]
    use std::collections::HashSet;
    #[cfg(unix)]
    use std::fs::{File, OpenOptions};
    #[cfg(unix)]
    use std::io::{ErrorKind, Read};
    #[cfg(unix)]
    use std::os::fd::AsRawFd;
    #[cfg(unix)]
    use std::os::unix::fs::{MetadataExt, OpenOptionsExt};
    #[cfg(unix)]
    use std::path::{Path, PathBuf};

    // Event types and codes in linux/input-event-codes.h.
    pub const EV_KEY: u16 = 0x01;
    pub const EV_ABS: u16 = 0x03;
    pub const BTN_SOUTH: u16 = 0x130;
    pub const BTN_EAST: u16 = 0x131;
    pub const BTN_DPAD_UP: u16 = 0x220;
    pub const BTN_DPAD_RIGHT: u16 = 0x223;
    pub const ABS_X: u16 = 0x00;
    pub const ABS_Y: u16 = 0x01;
    pub const ABS_HAT0X: u16 = 0x10;
    pub const ABS_HAT0Y: u16 = 0x11;

    /// What one event device has reported. The xpad driver reports the Xbox
    /// D-pad as a hat; other drivers report it as four buttons.
    #[derive(Clone, Debug)]
    pub struct Evdev {
        a: bool,
        b: bool,
        /// BTN_DPAD_UP, DOWN, LEFT and RIGHT.
        dpad: [bool; 4],
        hat: (i32, i32),
        stick: (f32, f32),
        /// The stick's raw ranges, as the device states them.
        range: [(i32, i32); 2],
    }

    impl Default for Evdev {
        fn default() -> Evdev {
            let range = (i16::MIN.into(), i16::MAX.into());
            Evdev { a: false, b: false, dpad: [false; 4], hat: (0, 0), stick: (0.0, 0.0), range: [range; 2] }
        }
    }

    impl Evdev {
        pub fn set_range(&mut self, code: u16, min: i32, max: i32) {
            match code {
                ABS_X => self.range[0] = (min, max),
                ABS_Y => self.range[1] = (min, max),
                _ => {}
            }
        }

        /// One `struct input_event`: its type, code and value.
        pub fn event(&mut self, kind: u16, code: u16, value: i32) {
            match (kind, code) {
                (EV_KEY, BTN_SOUTH) => self.a = value != 0,
                (EV_KEY, BTN_EAST) => self.b = value != 0,
                (EV_KEY, BTN_DPAD_UP..=BTN_DPAD_RIGHT) => self.dpad[(code - BTN_DPAD_UP) as usize] = value != 0,
                (EV_ABS, ABS_X) => self.stick.0 = axis(value, self.range[0].0, self.range[0].1),
                (EV_ABS, ABS_Y) => self.stick.1 = axis(value, self.range[1].0, self.range[1].1),
                (EV_ABS, ABS_HAT0X) => self.hat.0 = value.signum(),
                (EV_ABS, ABS_HAT0Y) => self.hat.1 = value.signum(),
                _ => {}
            }
        }

        pub fn pad(&self) -> Pad {
            let [up, down, left, right] = self.dpad;
            Pad {
                up: up || self.hat.1 < 0,
                down: down || self.hat.1 > 0,
                left: left || self.hat.0 < 0,
                right: right || self.hat.0 > 0,
                a: self.a,
                b: self.b,
                x: self.stick.0,
                y: self.stick.1,
            }
        }
    }

    /// `struct input_event` on 64-bit Linux: a `timeval`, then type, code and value.
    #[cfg(unix)]
    const EVENT_SIZE: usize = 24;
    /// The key bits up to KEY_MAX (0x2ff).
    #[cfg(unix)]
    const KEY_BYTES: usize = 0x300 / 8;

    /// `_IOC(_IOC_READ, 'E', nr, size)` in linux/input.h: EVIOCGBIT is `0x20 + type`
    /// and EVIOCGABS `0x40 + axis`.
    #[cfg(unix)]
    const fn eviocg(nr: u16, size: usize) -> u64 {
        (2 << 30) | ((size as u64) << 16) | ((b'E' as u64) << 8) | nr as u64
    }

    #[cfg(unix)]
    struct Device {
        file: File,
        path: PathBuf,
        state: Evdev,
    }

    /// The controllers among `/dev/input/event*`. Reading them needs the access
    /// that logind gives the active session for joysticks.
    #[cfg(unix)]
    pub struct Pads {
        devices: Vec<Device>,
        /// Devices opened and found not to be controllers, by path and inode.
        others: HashSet<(PathBuf, u64)>,
        reads: u32,
    }

    #[cfg(unix)]
    impl Pads {
        pub fn new() -> Pads {
            Pads { devices: Vec::new(), others: HashSet::new(), reads: 0 }
        }

        pub fn read(&mut self) -> Pad {
            if self.reads % super::RESCAN == 0 {
                self.scan();
            }
            self.reads = self.reads.wrapping_add(1);
            let mut buffer = [0u8; EVENT_SIZE * 64];
            let mut pad = Pad::default();
            self.devices.retain_mut(|device| {
                loop {
                    match device.file.read(&mut buffer) {
                        Ok(0) => return false,
                        Ok(read) => {
                            for event in buffer[..read].chunks_exact(EVENT_SIZE) {
                                let kind = u16::from_ne_bytes([event[16], event[17]]);
                                let code = u16::from_ne_bytes([event[18], event[19]]);
                                let value = i32::from_ne_bytes([event[20], event[21], event[22], event[23]]);
                                device.state.event(kind, code, value);
                            }
                        }
                        Err(error) if error.kind() == ErrorKind::WouldBlock => break,
                        Err(error) if error.kind() == ErrorKind::Interrupted => continue,
                        // Unplugged.
                        Err(_) => return false,
                    }
                }
                pad = pad.merge(device.state.pad());
                true
            });
            pad
        }

        fn scan(&mut self) {
            let Ok(entries) = std::fs::read_dir("/dev/input") else { return };
            let mut present = HashSet::new();
            for entry in entries.flatten() {
                if !entry.file_name().to_string_lossy().starts_with("event") {
                    continue;
                }
                let Ok(metadata) = entry.metadata() else { continue };
                let path = entry.path();
                let key = (path.clone(), metadata.ino());
                present.insert(key.clone());
                if self.others.contains(&key) || self.devices.iter().any(|device| device.path == path) {
                    continue;
                }
                match open(&path) {
                    Ok(Some(device)) => self.devices.push(device),
                    Ok(None) => {
                        self.others.insert(key);
                    }
                    // Not readable yet, or not by this user: tried again next time.
                    Err(_) => {}
                }
            }
            self.others.retain(|key| present.contains(key));
        }
    }

    /// The device at `path` if it is a controller: one with an A button.
    #[cfg(unix)]
    fn open(path: &Path) -> std::io::Result<Option<Device>> {
        let file = OpenOptions::new().read(true).custom_flags(libc::O_NONBLOCK).open(path)?;
        let fd = file.as_raw_fd();
        let mut keys = [0u8; KEY_BYTES];
        if unsafe { libc::ioctl(fd, eviocg(0x20 + EV_KEY, KEY_BYTES) as _, keys.as_mut_ptr()) } < 0 {
            return Ok(None);
        }
        if keys[BTN_SOUTH as usize / 8] & (1 << (BTN_SOUTH % 8)) == 0 {
            return Ok(None);
        }
        let mut state = Evdev::default();
        for code in [ABS_X, ABS_Y, ABS_HAT0X, ABS_HAT0Y] {
            // struct input_absinfo: value, minimum, maximum, fuzz, flat, resolution.
            let mut info = [0i32; 6];
            if unsafe { libc::ioctl(fd, eviocg(0x40 + code, std::mem::size_of_val(&info)) as _, info.as_mut_ptr()) } >= 0 {
                state.set_range(code, info[1], info[2]);
                state.event(EV_ABS, code, info[0]);
            }
        }
        Ok(Some(Device { file, path: path.to_path_buf(), state }))
    }
}

#[cfg(unix)]
use evdev::Pads;
#[cfg(windows)]
use xinput::Pads;

/// Reads the controllers on a thread of their own for as long as the window lives.
pub fn start(app: &AppWindow) {
    let weak = app.as_weak();
    let _ = std::thread::Builder::new().name("gamepad".into()).spawn(move || {
        let mut pads = Pads::new();
        let mut navigator = Navigator::new();
        let started = Instant::now();
        loop {
            for nav in navigator.update(&pads.read(), started.elapsed()) {
                if weak.upgrade_in_event_loop(move |app| apply(&app, nav)).is_err() {
                    return;
                }
            }
            std::thread::sleep(POLL);
        }
    });
}

fn apply(app: &AppWindow, nav: Nav) {
    // The game has the controller while it starts and runs, and on Windows so
    // does any other program in front.
    if app.get_play_state() != PlayState::Idle {
        return;
    }
    #[cfg(windows)]
    if !crate::windows::in_front() {
        return;
    }
    let press = |key: Key| {
        let text = SharedString::from(key);
        app.window().dispatch_event(WindowEvent::KeyPressed { text: text.clone() });
        app.window().dispatch_event(WindowEvent::KeyReleased { text });
    };
    match nav {
        Nav::Next => press(Key::Tab),
        Nav::Previous => press(Key::Backtab),
        Nav::Press => press(Key::Return),
        Nav::Back => app.invoke_back(),
    }
}

#[cfg(test)]
mod tests {
    use super::evdev::*;
    use super::xinput::from_xinput;
    use super::*;

    const MS: Duration = Duration::from_millis(1);
    const NOTHING: [Nav; 0] = [];

    fn pad(change: impl FnOnce(&mut Pad)) -> Pad {
        let mut pad = Pad::default();
        change(&mut pad);
        pad
    }

    /// A navigator that has seen the buttons released.
    fn ready() -> Navigator {
        let mut navigator = Navigator::new();
        assert_eq!(navigator.update(&Pad::default(), Duration::ZERO), NOTHING);
        navigator
    }

    #[test]
    fn dpad_and_stick_move_forward_and_back() {
        for (input, expected) in [
            (pad(|p| p.down = true), Nav::Next),
            (pad(|p| p.right = true), Nav::Next),
            (pad(|p| p.y = 0.9), Nav::Next),
            (pad(|p| p.x = 0.9), Nav::Next),
            (pad(|p| p.up = true), Nav::Previous),
            (pad(|p| p.left = true), Nav::Previous),
            (pad(|p| p.y = -0.9), Nav::Previous),
            (pad(|p| p.x = -0.9), Nav::Previous),
        ] {
            assert_eq!(ready().update(&input, 16 * MS), [expected], "{input:?}");
        }
    }

    #[test]
    fn a_small_push_or_two_ways_at_once_do_nothing() {
        assert_eq!(ready().update(&pad(|p| p.y = 0.3), 16 * MS), NOTHING);
        assert_eq!(ready().update(&pad(|p| { p.up = true; p.down = true; }), 16 * MS), NOTHING);
        assert_eq!(ready().update(&pad(|p| { p.left = true; p.y = 0.9; }), 16 * MS), NOTHING);
    }

    #[test]
    fn a_held_direction_repeats_after_a_pause() {
        let mut navigator = ready();
        let down = pad(|p| p.down = true);
        assert_eq!(navigator.update(&down, 100 * MS), [Nav::Next]);
        assert_eq!(navigator.update(&down, 400 * MS), NOTHING);
        assert_eq!(navigator.update(&down, 550 * MS), [Nav::Next]);
        assert_eq!(navigator.update(&down, 650 * MS), NOTHING);
        assert_eq!(navigator.update(&down, 690 * MS), [Nav::Next]);
        // Let go and press again: it moves at once.
        assert_eq!(navigator.update(&Pad::default(), 700 * MS), NOTHING);
        assert_eq!(navigator.update(&down, 716 * MS), [Nav::Next]);
        // The other way moves at once too.
        assert_eq!(navigator.update(&pad(|p| p.up = true), 732 * MS), [Nav::Previous]);
    }

    #[test]
    fn the_stick_lets_go_only_well_back_from_the_edge() {
        let mut navigator = ready();
        assert_eq!(navigator.update(&pad(|p| p.y = 0.6), 16 * MS), [Nav::Next]);
        // Wobbling around the threshold is the same push, not a new one.
        assert_eq!(navigator.update(&pad(|p| p.y = 0.45), 32 * MS), NOTHING);
        assert_eq!(navigator.update(&pad(|p| p.y = 0.55), 48 * MS), NOTHING);
        assert_eq!(navigator.update(&pad(|p| p.y = 0.2), 64 * MS), NOTHING);
        assert_eq!(navigator.update(&pad(|p| p.y = 0.55), 80 * MS), [Nav::Next]);
    }

    #[test]
    fn a_presses_and_b_goes_back_once_per_press() {
        let mut navigator = ready();
        let a = pad(|p| p.a = true);
        assert_eq!(navigator.update(&a, 16 * MS), [Nav::Press]);
        assert_eq!(navigator.update(&a, 32 * MS), NOTHING);
        assert_eq!(navigator.update(&a, 2000 * MS), NOTHING);
        assert_eq!(navigator.update(&Pad::default(), 2016 * MS), NOTHING);
        assert_eq!(navigator.update(&a, 2032 * MS), [Nav::Press]);
        let b = pad(|p| p.b = true);
        assert_eq!(navigator.update(&b, 2048 * MS), [Nav::Back]);
        assert_eq!(navigator.update(&b, 2064 * MS), NOTHING);
    }

    #[test]
    fn a_button_held_at_start_waits_for_its_release() {
        let mut navigator = Navigator::new();
        let a = pad(|p| p.a = true);
        assert_eq!(navigator.update(&a, Duration::ZERO), NOTHING);
        assert_eq!(navigator.update(&Pad::default(), 16 * MS), NOTHING);
        assert_eq!(navigator.update(&a, 32 * MS), [Nav::Press]);
    }

    #[test]
    fn two_controllers_count_as_one() {
        let merged = pad(|p| { p.a = true; p.x = 0.2; }).merge(pad(|p| { p.down = true; p.x = -0.7; }));
        assert_eq!(merged, pad(|p| { p.a = true; p.down = true; p.x = -0.7; }));
    }

    #[test]
    fn xinput_buttons_and_stick() {
        assert_eq!(from_xinput(0x0002 | 0x1000, 0, 0), pad(|p| { p.down = true; p.a = true; }));
        assert_eq!(from_xinput(0x0001 | 0x0004 | 0x2000, 0, 0), pad(|p| { p.up = true; p.left = true; p.b = true; }));
        assert!(from_xinput(0x0008, 0, 0).right);
        // X, Y, the shoulders, Start and Back move nothing.
        let others = from_xinput(0x4000 | 0x8000 | 0x0100 | 0x0200 | 0x0010 | 0x0020, 0, 0);
        assert_eq!(Pad { x: 0.0, y: 0.0, ..others }, Pad::default());
        // XInput's stick points up for a positive y; the pad's points down.
        let up = from_xinput(0, 0, i16::MAX);
        assert!(up.y < -0.99 && up.x.abs() < 0.001, "{up:?}");
        let left = from_xinput(0, i16::MIN, 0);
        assert!(left.x <= -0.99 && left.y.abs() < 0.001, "{left:?}");
        assert_eq!(ready().update(&up, 16 * MS), [Nav::Previous]);
        assert_eq!(ready().update(&from_xinput(0, 0, 0), 16 * MS), NOTHING);
    }

    #[test]
    fn evdev_buttons_hat_and_stick() {
        let mut device = Evdev::default();
        device.event(EV_KEY, BTN_SOUTH, 1);
        assert_eq!(device.pad(), pad(|p| p.a = true));
        device.event(EV_KEY, BTN_SOUTH, 0);
        device.event(EV_KEY, BTN_EAST, 1);
        assert_eq!(device.pad(), pad(|p| p.b = true));
        device.event(EV_KEY, BTN_EAST, 0);
        // The xpad D-pad is a hat from -1 to 1.
        device.event(EV_ABS, ABS_HAT0Y, 1);
        assert_eq!(device.pad(), pad(|p| p.down = true));
        device.event(EV_ABS, ABS_HAT0Y, 0);
        device.event(EV_ABS, ABS_HAT0X, -1);
        assert_eq!(device.pad(), pad(|p| p.left = true));
        device.event(EV_ABS, ABS_HAT0X, 0);
        // Other drivers send BTN_DPAD_UP, DOWN, LEFT (0x222) and RIGHT (0x223).
        device.event(EV_KEY, 0x222, 1);
        assert_eq!(device.pad(), pad(|p| p.left = true));
        device.event(EV_KEY, 0x222, 0);
        device.event(EV_KEY, BTN_DPAD_RIGHT, 1);
        assert_eq!(device.pad(), pad(|p| p.right = true));
        device.event(EV_KEY, BTN_DPAD_RIGHT, 0);
        assert_eq!(device.pad(), Pad::default());
        // The stick, in the range the device states.
        device.set_range(ABS_Y, 0, 255);
        device.event(EV_ABS, ABS_Y, 255);
        assert_eq!(device.pad().y, 1.0);
        device.event(EV_ABS, ABS_Y, 0);
        assert_eq!(device.pad().y, -1.0);
        device.event(EV_ABS, ABS_X, i16::MAX.into());
        assert!(device.pad().x > 0.99);
        // Other buttons and axes (BTN_NORTH, ABS_RX) are ignored.
        let before = device.pad();
        device.event(EV_KEY, 0x133, 1);
        device.event(EV_ABS, 0x03, 9000);
        assert_eq!(device.pad(), before);
    }

    #[test]
    fn axes_without_a_range_read_as_centred() {
        assert_eq!(axis(5, 0, 0), 0.0);
        assert_eq!(axis(-1, -1, 1), -1.0);
        assert_eq!(axis(40000, -32768, 32767), 1.0);
    }
}
