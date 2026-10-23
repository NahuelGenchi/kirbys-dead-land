//! `--screenshot DIR`: every screen drawn offscreen with sample data, for review.
//! Without `--art-from`, Kirby and the ability icons are placeholders, so the
//! pictures hold no art from the game and can be shared.

use crate::{AppWindow, Ability, CheckItem, PlayState, Screen, SetupState, StepItem, Tone};
use slint::platform::software_renderer::{MinimalSoftwareWindow, PremultipliedRgbaColor, RepaintBufferType, TargetPixel};
use slint::{ComponentHandle, Image, ModelRc, SharedString, VecModel};
use std::path::{Path, PathBuf};
use std::rc::Rc;
use std::time::Duration;

struct Offscreen {
    window: Rc<MinimalSoftwareWindow>,
}

static CLOCK_MS: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(5400);

impl slint::platform::Platform for Offscreen {
    fn create_window_adapter(&self) -> Result<Rc<dyn slint::platform::WindowAdapter>, slint::PlatformError> {
        Ok(self.window.clone())
    }
    fn duration_since_start(&self) -> Duration {
        // A clock that only moves between frames, so every run draws the same pictures.
        Duration::from_millis(CLOCK_MS.load(std::sync::atomic::Ordering::Relaxed))
    }
}

#[derive(Clone, Copy, Default)]
struct Rgba([u8; 4]);

impl TargetPixel for Rgba {
    fn blend(&mut self, c: PremultipliedRgbaColor) {
        let k = 255 - c.alpha as u16;
        self.0[0] = (c.red as u16 + self.0[0] as u16 * k / 255) as u8;
        self.0[1] = (c.green as u16 + self.0[1] as u16 * k / 255) as u8;
        self.0[2] = (c.blue as u16 + self.0[2] as u16 * k / 255) as u8;
        self.0[3] = 255;
    }
    fn from_rgb(r: u8, g: u8, b: u8) -> Self {
        Rgba([r, g, b, 255])
    }
}

pub fn write_png(path: &Path, width: u32, height: u32, rgba: &[u8]) -> std::io::Result<()> {
    let file = std::io::BufWriter::new(std::fs::File::create(path)?);
    let mut encoder = png::Encoder::new(file, width, height);
    encoder.set_color(png::ColorType::Rgba);
    encoder.set_depth(png::BitDepth::Eight);
    let mut writer = encoder.write_header().map_err(std::io::Error::other)?;
    writer.write_image_data(rgba).map_err(std::io::Error::other)
}

fn model<T: Clone + 'static>(items: Vec<T>) -> ModelRc<T> {
    ModelRc::new(VecModel::from(items))
}

fn check(id: &str, title: &str, detail: &str, tone: Tone, action: &str, secondary: &str, command: &str) -> CheckItem {
    CheckItem {
        id: id.into(),
        number: ["steam", "game", "rom", "bwrap"].iter().position(|known| *known == id).map_or(0, |i| i as i32 + 1),
        title: title.into(),
        detail: detail.into(),
        tone,
        action: action.into(),
        secondary: secondary.into(),
        command: command.into(),
    }
}

fn step(title: &str, detail: &str, tone: Tone, elapsed: &str) -> StepItem {
    StepItem { title: title.into(), detail: detail.into(), tone, elapsed: elapsed.into() }
}

const ABILITIES: [&str; 24] = ["Cutter", "Mirror", "Beam", "Fighter", "Suplex", "YoYo", "Wheel", "Bomb", "Ice", "Fire",
    "Plasma", "Ninja", "Stone", "Wing", "Jet", "Copy", "Sword", "Hammer", "Parasol", "Paint", "Mike", "Crash", "Cook", "Sleep"];

fn steps(current: usize, failed: bool) -> Vec<StepItem> {
    let all = [
        ("Download the helpers", "Python 3.14.8 · 96 MB of 165 MB", "0:25"),
        ("Install the mod", "Kirby's Dead Land 0.2.0", "0:01"),
        ("Start Kirby's adventure", "Original menus, part 8 of 26: player menu", "0:13"),
        ("Read Kirby's sprites", "Decoding bkir", "0:23"),
        ("Collect the copy abilities", "Ability 9 of 24: Ice", "0:23"),
        ("Sew Kirby's outfit", "Fitting Kirby into the hero's place", "0:03"),
        ("Put it all together", "Placing the mod beside your game", "0:01"),
    ];
    all.iter()
        .enumerate()
        .map(|(i, (title, detail, time))| {
            if i < current {
                step(title, "", Tone::Ok, time)
            } else if i == current {
                if failed {
                    step(title, "Stopped: the bridge could not start", Tone::Error, "")
                } else {
                    step(title, detail, Tone::Busy, "")
                }
            } else {
                step(title, "", Tone::Pending, "")
            }
        })
        .collect()
}

fn art(app: &AppWindow, root: Option<&Path>) {
    let mut abilities = Vec::new();
    match root {
        Some(root) => {
            let frames = root.join("rom/decoded/bkir/frames");
            if let (Ok(kirby), Ok(happy)) = (Image::load_from_path(&frames.join("0041.png")), Image::load_from_path(&frames.join("0009.png"))) {
                app.set_kirby(kirby);
                app.set_kirby_happy(happy);
                app.set_has_kirby(true);
            }
            for name in ABILITIES {
                if let Ok(icon) = Image::load_from_path(&root.join(format!("ability-icons/{name}-32.png"))) {
                    abilities.push(Ability { name: name.into(), icon });
                }
            }
        }
        None => {
            let art = crate::ART.with(|art| art.clone());
            for name in ABILITIES {
                let icon = art.pill(30.0, 30.0, 9.0, slint::Color::from_argb_u8(60, 255, 255, 255), slint::Color::from_argb_u8(30, 255, 255, 255));
                abilities.push(Ability { name: name.into(), icon });
            }
        }
    }
    app.set_abilities(model(abilities));
}

fn checks_first() -> Vec<CheckItem> {
    vec![
        check("steam", "Steam", "Installed", Tone::Ok, "", "", ""),
        check("game", "Dead Cells", "Installed from Steam · v35, the tested build", Tone::Ok, "", "", ""),
        check("rom", "Your Kirby Super Star Ultra", "Found “Kirby Super Star Ultra (USA).nds” in Documents. Use this copy?", Tone::Pending, "Use this file", "Choose another", ""),
        check("bwrap", "Sandbox helper", "bubblewrap keeps your games read-only while you play", Tone::Ok, "", "", ""),
    ]
}

fn reset(app: &AppWindow) {
    app.set_settings_open(false);
    app.set_licenses_open(false);
    app.set_question_id(SharedString::new());
    app.set_update_version(SharedString::new());
    app.set_toast_shown(false);
    app.set_setup_details(false);
    app.set_checks_blocked(false);
}

/// `scale` is the pixel density: 2.0 draws the 1040×680 window as 2080×1360 pixels.
pub fn run(out: &Path, art_root: Option<PathBuf>, scale: f32) -> Result<(), String> {
    std::fs::create_dir_all(out).map_err(|e| e.to_string())?;
    let window = MinimalSoftwareWindow::new(RepaintBufferType::NewBuffer);
    slint::platform::set_platform(Box::new(Offscreen { window: window.clone() })).map_err(|e| e.to_string())?;
    let app = AppWindow::new().map_err(|e| e.to_string())?;
    crate::connect_art(&app);
    window.dispatch_event(slint::platform::WindowEvent::ScaleFactorChanged { scale_factor: scale });
    let (w, h) = ((1040.0 * scale).round() as u32, (680.0 * scale).round() as u32);
    window.set_size(slint::PhysicalSize::new(w, h));
    app.show().map_err(|e| e.to_string())?;
    app.set_version(env!("CARGO_PKG_VERSION").into());
    app.set_game_detail("Dead Cells v35".into());
    app.set_rom_name("Kirby Super Star Ultra (USA).nds".into());
    app.set_rom_detail("USA · revision 0 · verified\n~/Documents/ROMS/Nintendo DS".into());
    app.set_game_path("~/.local/share/Steam/steamapps/common/Dead Cells".into());
    app.set_data_detail("1.4 GB in ~/.local/share/kirbys-dead-land".into());
    app.set_update_detail("You have the latest version".into());
    app.set_update_action("Check now".into());
    app.set_licenses(crate::NOTICES.into());
    art(&app, art_root.as_deref());

    let scenes: Vec<(&str, Box<dyn Fn(&AppWindow)>)> = vec![
        ("01-checks-first-run", Box::new(|app: &AppWindow| {
            app.set_screen(Screen::Checks);
            app.set_checks(model(checks_first()));
            app.set_checks_ready(false);
            app.set_setup_done(false);
        })),
        ("02-checks-problems", Box::new(|app: &AppWindow| {
            app.set_screen(Screen::Checks);
            app.set_checks(model(vec![
                check("steam", "Steam", "Installed (Flatpak)", Tone::Ok, "", "", ""),
                check("game", "Dead Cells", "Steam installed the Windows version. In Steam, open Dead Cells › Properties › Compatibility and turn off the compatibility tool.", Tone::Error, "Open in Steam", "", ""),
                check("rom", "Your Kirby Super Star Ultra", "This is the European version. The mod needs the USA version (revision 0).", Tone::Error, "Choose another file", "", ""),
                check("bwrap", "Sandbox helper", "Install bubblewrap once with this command, then check again:", Tone::Error, "", "", "sudo dnf install bubblewrap"),
            ]));
            app.set_checks_blocked(true);
            app.set_checks_ready(false);
            app.set_setup_done(false);
        })),
        ("03-checks-ready", Box::new(|app: &AppWindow| {
            app.set_screen(Screen::Checks);
            let mut items = checks_first();
            items[2] = check("rom", "Your Kirby Super Star Ultra", "USA · revision 0 · verified", Tone::Ok, "", "Change", "");
            items[1] = check("game", "Dead Cells", "Build 24100332 isn't the tested build (23762174). It will probably work; if not, look for a launcher update.", Tone::Warn, "", "", "");
            app.set_checks(model(items));
            app.set_checks_ready(true);
            app.set_setup_done(false);
        })),
        ("04-setup-running", Box::new(|app: &AppWindow| {
            app.set_screen(Screen::Setup);
            app.set_steps(model(steps(2, false)));
            app.set_current_step(2);
            app.set_setup_progress(0.38);
            app.set_setup_state(SetupState::Running);
            app.set_setup_remaining("about a minute left".into());
        })),
        ("05-setup-details", Box::new(|app: &AppWindow| {
            app.set_screen(Screen::Setup);
            app.set_steps(model(steps(4, false)));
            app.set_current_step(4);
            app.set_setup_progress(0.64);
            app.set_setup_state(SetupState::Running);
            app.set_setup_remaining("less than a minute left".into());
            app.set_setup_details(true);
            app.set_setup_log("== Collect the copy abilities\n$ python3 tools/capture_ability_icons.py\nCutter: steady after 41 frames\nMirror: steady after 38 frames\nBeam: steady after 44 frames\nFighter: steady after 40 frames\nSuplex: steady after 39 frames\nYoYo: steady after 42 frames\nWheel: steady after 37 frames\nBomb: steady after 45 frames\n".into());
        })),
        ("06-setup-failed", Box::new(|app: &AppWindow| {
            app.set_screen(Screen::Setup);
            app.set_steps(model(steps(4, true)));
            app.set_current_step(4);
            app.set_setup_progress(0.57);
            app.set_setup_state(SetupState::Failed);
            app.set_setup_error("Collecting the copy abilities stopped: the emulator ran out of memory. Close other programs and try again.".into());
        })),
        ("07-home", Box::new(|app: &AppWindow| {
            app.set_screen(Screen::Home);
            app.set_play_state(PlayState::Idle);
            app.set_setup_done(true);
        })),
        ("08-home-starting-steam", Box::new(|app: &AppWindow| {
            app.set_screen(Screen::Home);
            app.set_play_state(PlayState::StartingSteam);
        })),
        ("09-home-playing", Box::new(|app: &AppWindow| {
            app.set_screen(Screen::Home);
            app.set_play_state(PlayState::Running);
        })),
        ("10-home-update", Box::new(|app: &AppWindow| {
            app.set_screen(Screen::Home);
            app.set_play_state(PlayState::Idle);
            app.set_update_version("0.3.0".into());
        })),
        ("11-settings", Box::new(|app: &AppWindow| {
            app.set_screen(Screen::Home);
            app.set_settings_open(true);
        })),
        ("12-licenses", Box::new(|app: &AppWindow| {
            app.set_screen(Screen::Home);
            app.set_licenses_open(true);
        })),
        ("13-low-memory", Box::new(|app: &AppWindow| {
            app.set_screen(Screen::Home);
            app.set_question_title("Your computer is low on memory".into());
            app.set_question_body("Dead Cells with Kirby needs about 6 GB of free memory, and 3.1 GB is free now. Close other programs for a smoother game, or play anyway.".into());
            app.set_question_confirm("Play anyway".into());
            app.set_question_cancel("Not now".into());
            app.set_question_warning(true);
            app.set_question_id("memory".into());
        })),
        ("14-copied", Box::new(|app: &AppWindow| {
            app.set_screen(Screen::Checks);
            let mut items = checks_first();
            items[3] = check("bwrap", "Sandbox helper", "Install bubblewrap once with this command, then check again:", Tone::Error, "", "", "sudo apt install bubblewrap");
            app.set_checks(model(items));
            app.set_checks_blocked(true);
            app.set_checks_ready(false);
            app.set_setup_done(false);
            app.set_toast("Copied".into());
            app.set_toast_shown(true);
        })),
    ];
    for (name, scene) in scenes {
        reset(&app);
        scene(&app);
        let mut buffer = vec![Rgba::default(); (w * h) as usize];
        // Draw once so transitions start, then again once they have finished.
        for _ in 0..2 {
            slint::platform::update_timers_and_animations();
            window.request_redraw();
            window.draw_if_needed(|renderer| {
                renderer.render(&mut buffer, w as usize);
            });
            CLOCK_MS.fetch_add(1000, std::sync::atomic::Ordering::Relaxed);
        }
        let bytes: Vec<u8> = buffer.iter().flat_map(|p| p.0).collect();
        write_png(&out.join(format!("{name}.png")), w, h, &bytes).map_err(|e| e.to_string())?;
        println!("{}", out.join(format!("{name}.png")).display());
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::Offscreen;
    use slint::platform::software_renderer::{MinimalSoftwareWindow, RepaintBufferType};
    use slint::platform::{PointerEventButton, WindowEvent};
    use slint::ComponentHandle;

    slint::slint! {
        // The app's own fonts, as in app.slint: a test program may find no system font (Wine).
        import "ui/fonts/Fredoka.ttf";
        import "ui/fonts/Nunito.ttf";
        import { Theme } from "ui/theme.slint";
        import { PrimaryButton, SecondaryButton, LinkButton, IconButton } from "ui/widgets.slint";
        export component Clicks inherits Window {
            width: 400px;
            height: 200px;
            default-font-family: Theme.body;
            in-out property <[int]> counts: [0, 0, 0, 0];
            PrimaryButton { x: 0; y: 0; width: 200px; height: 50px; text: "Primary"; clicked => { root.counts[0] += 1; } }
            SecondaryButton { x: 200px; y: 0; width: 200px; height: 50px; text: "Secondary"; clicked => { root.counts[1] += 1; } }
            LinkButton { x: 0; y: 100px; width: 200px; height: 50px; text: "Link"; clicked => { root.counts[2] += 1; } }
            IconButton { x: 200px; y: 100px; label: "Icon"; clicked => { root.counts[3] += 1; } }
        }
    }

    /// One press and release on each button runs it once (#258): the keyboard
    /// focus scope above each button's touch area must not take the first click.
    #[test]
    fn one_click_runs_each_button() {
        let window = MinimalSoftwareWindow::new(RepaintBufferType::NewBuffer);
        slint::platform::set_platform(Box::new(Offscreen { window: window.clone() })).unwrap();
        let clicks = Clicks::new().unwrap();
        window.set_size(slint::PhysicalSize::new(400, 200));
        clicks.show().unwrap();
        for (x, y) in [(100.0, 25.0), (300.0, 25.0), (100.0, 125.0), (220.0, 120.0)] {
            let position = slint::LogicalPosition::new(x, y);
            window.dispatch_event(WindowEvent::PointerMoved { position });
            window.dispatch_event(WindowEvent::PointerPressed { position, button: PointerEventButton::Left });
            window.dispatch_event(WindowEvent::PointerReleased { position, button: PointerEventButton::Left });
            slint::platform::update_timers_and_animations();
        }
        let counts: Vec<i32> = slint::Model::iter(&clicks.get_counts()).collect();
        assert_eq!(counts, [1, 1, 1, 1], "Primary, Secondary, Link and Icon buttons after one click each");
    }
}
