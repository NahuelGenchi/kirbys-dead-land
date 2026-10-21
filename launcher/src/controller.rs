//! Connects the window to the checks, the setup and the game.

use crate::config::{Config, SetupRecord};
use crate::paths::{self, Bundle};
use crate::rom::{self, Verdict};
use crate::setup::{self, Control};
use crate::steam::{self, GameStatus, Steam};
use crate::system::{self, Sandbox};
use crate::{play, update, Ability, AppWindow, CheckItem, PlayState, Screen, SetupState, StepItem, Tone};
use slint::{ComponentHandle, Image, ModelRc, SharedString, VecModel, Weak};
use std::collections::VecDeque;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use std::time::Duration;

const ABILITIES: [&str; 24] = ["Cutter", "Mirror", "Beam", "Fighter", "Suplex", "YoYo", "Wheel", "Bomb", "Ice", "Fire",
    "Plasma", "Ninja", "Stone", "Wing", "Jet", "Copy", "Sword", "Hammer", "Parasol", "Paint", "Mike", "Crash", "Cook", "Sleep"];
const ROM_TITLE: &str = "Your Kirby Super Star Ultra";
const MEMORY_WANTED: u64 = 6 * 1024 * 1024 * 1024;
const HELP: &str = "https://github.com/NahuelGenchi/kirbys-dead-land/blob/main/docs/LAUNCHER.md";

pub struct Options {
    pub bundle: Option<PathBuf>,
    /// For checks only: Play runs a bounded offscreen game with automated input.
    pub smoke_seconds: Option<u32>,
}

#[derive(Default)]
struct Found {
    /// False until the first look at the system has finished.
    checked: bool,
    steams: Vec<Steam>,
    game: Option<GameStatus>,
    verdict: Option<Verdict>,
    suggestion: Option<PathBuf>,
    sandbox: Option<Sandbox>,
}

struct State {
    root: PathBuf,
    config_dir: PathBuf,
    config: Config,
    bundle: Option<Bundle>,
    found: Found,
    /// A file the player picked that is not the right ROM.
    rejected: Option<(PathBuf, Verdict)>,
    record: SetupRecord,
    setup: Option<Control>,
    setup_steps: Vec<StepItem>,
    log: VecDeque<String>,
    game_pid: Option<u32>,
    last_game_log: Option<PathBuf>,
    update: Option<update::Release>,
    smoke_seconds: Option<u32>,
    first_check: bool,
}

#[derive(Clone)]
pub struct Controller {
    weak: Weak<AppWindow>,
    state: Arc<Mutex<State>>,
}

fn model<T: Clone + 'static>(items: Vec<T>) -> ModelRc<T> {
    ModelRc::new(VecModel::from(items))
}

fn item(id: &str, number: i32, title: &str, detail: impl Into<SharedString>, tone: Tone) -> CheckItem {
    CheckItem { id: id.into(), number, title: title.into(), detail: detail.into(), tone, ..Default::default() }
}

fn minutes(duration: Duration) -> String {
    let seconds = duration.as_secs();
    format!("{}:{:02}", seconds / 60, seconds % 60)
}

fn remaining(duration: Option<Duration>) -> String {
    match duration.map(|d| d.as_secs()) {
        None => "starting".into(),
        Some(s) if s < 50 => "less than a minute left".into(),
        Some(s) if s < 90 => "about a minute left".into(),
        Some(s) => format!("about {} min left", (s + 30) / 60),
    }
}

fn folder_name(path: &Path) -> String {
    path.parent()
        .map(|dir| match dir.strip_prefix(paths::home()) {
            Ok(rest) => rest.components().next().map(|c| c.as_os_str().to_string_lossy().into_owned()).unwrap_or_else(|| "your home folder".into()),
            Err(_) => paths::pretty(dir),
        })
        .unwrap_or_default()
}

fn file_name(path: &Path) -> String {
    path.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default()
}

/// The check rows and whether everything needed to play is in place.
fn rows(state: &State) -> (Vec<CheckItem>, bool) {
    let found = &state.found;
    let mut rows = Vec::new();
    let mut ready = true;
    if state.bundle.is_none() {
        ready = false;
        let mut row = item("bundle", 0, "Launcher files", "This launcher is missing the mod's files. Download it again from the releases page.", Tone::Error);
        row.action = "Open downloads".into();
        rows.push(row);
    }
    if !found.checked {
        for (id, number, title) in [("steam", 1, "Steam"), ("game", 2, "Dead Cells"), ("rom", 3, ROM_TITLE), ("bwrap", 4, "Sandbox helper")] {
            rows.push(item(id, number, title, "Checking…", Tone::Busy));
        }
        return (rows, false);
    }

    let game_steam = match &found.game {
        Some(GameStatus::Ready(g) | GameStatus::Windows(g) | GameStatus::Incomplete(g)) => Some(g.steam.kind),
        _ => found.steams.first().map(|s| s.kind),
    };
    if found.steams.is_empty() {
        ready = false;
        let mut row = item("steam", 1, "Steam", "Steam isn't installed. Install it and sign in once, then check again.", Tone::Error);
        row.action = "Get Steam".into();
        rows.push(row);
    } else {
        let detail = match game_steam {
            Some(steam::Kind::Flatpak) => "Installed (Flatpak)",
            Some(steam::Kind::Snap) => "Installed (Snap)",
            _ => "Installed",
        };
        rows.push(item("steam", 1, "Steam", detail, Tone::Ok));
    }

    let game = match &found.game {
        _ if found.steams.is_empty() => item("game", 2, "Dead Cells", "Install Steam first.", Tone::Pending),
        Some(GameStatus::Ready(game)) => match game.build.as_deref() {
            Some(steam::TESTED_BUILD) | None => item("game", 2, "Dead Cells", "Installed from Steam · v35, the tested build", Tone::Ok),
            Some(build) => item(
                "game", 2, "Dead Cells",
                format!("Build {build} isn't the tested build ({}). It will probably work; if not, look for a launcher update.", steam::TESTED_BUILD),
                Tone::Warn,
            ),
        },
        Some(GameStatus::Windows(_)) => {
            ready = false;
            let mut row = item("game", 2, "Dead Cells", "Steam installed the Windows version. In Steam, open Dead Cells › Properties › Compatibility and turn off the compatibility tool.", Tone::Error);
            row.action = "Open in Steam".into();
            row
        }
        Some(GameStatus::Incomplete(_)) => {
            ready = false;
            let mut row = item("game", 2, "Dead Cells", "Dead Cells isn't fully installed. Let Steam finish, or verify its files in Steam.", Tone::Error);
            row.action = "Open in Steam".into();
            row
        }
        _ => {
            ready = false;
            let mut row = item("game", 2, "Dead Cells", "Dead Cells isn't installed in Steam yet. You need it to play.", Tone::Error);
            row.action = "Install in Steam".into();
            row
        }
    };
    rows.push(game);

    let title = ROM_TITLE;
    let rom = match (&state.rejected, &state.config.rom, &found.verdict) {
        (Some((path, verdict)), _, _) => {
            let mut row = item("rom", 3, title, format!("“{}”: {}", file_name(path), verdict.message()), Tone::Error);
            row.action = "Choose another file".into();
            row
        }
        (None, Some(_), Some(Verdict::Verified)) => {
            let mut row = item("rom", 3, title, Verdict::Verified.message(), Tone::Ok);
            row.secondary = "Change".into();
            row
        }
        (None, Some(path), Some(verdict)) if found.suggestion.is_none() => {
            let text = if *verdict == Verdict::Missing {
                format!("Your ROM is no longer at {}. Choose it again.", paths::pretty(path))
            } else {
                verdict.message()
            };
            let mut row = item("rom", 3, title, text, Tone::Error);
            row.action = "Choose file".into();
            row
        }
        _ => match &found.suggestion {
            Some(path) => {
                let mut row = item("rom", 3, title, format!("Found “{}” in {}. Use this copy?", file_name(path), folder_name(path)), Tone::Pending);
                row.action = "Use this file".into();
                row.secondary = "Choose another".into();
                row
            }
            None => {
                let mut row = item("rom", 3, title, "Choose your copy of the USA version, a .nds file. It stays where it is and is only read.", Tone::Pending);
                row.action = "Choose file".into();
                row
            }
        },
    };
    ready &= rom.tone == Tone::Ok;
    rows.push(rom);

    let sandbox = match &found.sandbox {
        Some(Sandbox::Ready) => item("bwrap", 4, "Sandbox helper", "bubblewrap keeps your games read-only while you play", Tone::Ok),
        Some(Sandbox::Missing) => {
            ready = false;
            match system::install_command(&system::distribution(), "bubblewrap") {
                Some(command) => {
                    let mut row = item("bwrap", 4, "Sandbox helper", "Install bubblewrap once with this command, then check again:", Tone::Error);
                    row.command = command.into();
                    row
                }
                None => item("bwrap", 4, "Sandbox helper", "Install the bubblewrap package with your system's software manager, then check again.", Tone::Error),
            }
        }
        Some(Sandbox::Blocked(why)) => {
            ready = false;
            let mut row = item("bwrap", 4, "Sandbox helper", format!("bubblewrap is installed, but your system blocks its sandbox: {why}."), Tone::Error);
            row.action = "How to fix".into();
            row
        }
        None => {
            ready = false;
            item("bwrap", 4, "Sandbox helper", "Checking…", Tone::Busy)
        }
    };
    rows.push(sandbox);
    (rows, ready)
}

impl Controller {
    pub fn new(app: &AppWindow, options: Options) -> Controller {
        let root = paths::data_root();
        let config_dir = paths::config_dir();
        let config = Config::load(&config_dir);
        let state = State {
            record: SetupRecord::load(&root),
            root,
            config_dir,
            bundle: paths::find_bundle(options.bundle),
            config,
            found: Found::default(),
            rejected: None,
            setup: None,
            setup_steps: Vec::new(),
            log: VecDeque::new(),
            game_pid: None,
            last_game_log: None,
            update: None,
            smoke_seconds: options.smoke_seconds,
            first_check: true,
        };
        let controller = Controller { weak: app.as_weak(), state: Arc::new(Mutex::new(state)) };
        controller.connect(app);
        controller
    }

    fn with_app(&self, f: impl FnOnce(&AppWindow, &Controller) + Send + 'static) {
        let me = self.clone();
        let _ = self.weak.upgrade_in_event_loop(move |app| f(&app, &me));
    }

    fn connect(&self, app: &AppWindow) {
        let state = self.state.lock().unwrap();
        app.set_version(env!("CARGO_PKG_VERSION").into());
        app.set_auto_update(state.config.auto_update);
        app.set_licenses(crate::NOTICES.into());
        app.global::<crate::Theme>().set_mono(system::monospace_family().into());
        app.set_data_detail(format!("In {}", paths::pretty(&state.root)).into());
        app.set_update_detail("Not checked yet".into());
        app.set_update_action("Check now".into());
        drop(state);

        let c = self.clone();
        app.on_check_action(move |id, which| c.check_action(&id, &which));
        let c = self.clone();
        app.on_recheck(move || c.run_checks());
        let c = self.clone();
        app.on_checks_next(move || c.next());
        let c = self.clone();
        app.on_setup_cancel(move || c.cancel_setup());
        let c = self.clone();
        app.on_setup_retry(move || c.start_setup(false));
        let c = self.clone();
        app.on_setup_back(move || {
            c.with_app(|app, _| app.set_screen(Screen::Checks));
            c.run_checks();
        });
        let c = self.clone();
        app.on_copy_log(move || {
            let text: Vec<String> = c.state.lock().unwrap().log.iter().cloned().collect();
            if let Some(app) = c.weak.upgrade() {
                app.invoke_copy_text(text.join("\n").into());
            }
        });
        let c = self.clone();
        app.on_play(move || c.play(false));
        let c = self.clone();
        app.on_stop_game(move || c.stop_game());
        let c = self.clone();
        app.on_open_release(move || {
            if let Some(release) = &c.state.lock().unwrap().update {
                let _ = system::open(&release.url);
            }
        });
        let c = self.clone();
        app.on_settings_action(move |which| c.settings_action(&which));
        let c = self.clone();
        app.on_auto_update_changed(move |value| {
            let mut state = c.state.lock().unwrap();
            state.config.auto_update = value;
            let _ = state.config.save(&state.config_dir);
        });
        let c = self.clone();
        app.on_answer(move |id, yes| c.answer(&id, yes));

        let c = self.clone();
        app.window().on_close_requested(move || {
            if let Some(control) = c.state.lock().unwrap().setup.take() {
                control.cancel();
            }
            slint::CloseRequestResponse::HideWindow
        });
    }

    pub fn start(&self) {
        self.run_checks();
        if self.state.lock().unwrap().config.auto_update {
            self.check_update(false);
        }
    }

    // Checks

    pub fn run_checks(&self) {
        self.with_app(|app, c| {
            app.set_checking(true);
            let (rows, _) = rows(&c.state.lock().unwrap());
            app.set_checks(model(rows));
        });
        let me = self.clone();
        std::thread::spawn(move || {
            let home = paths::home();
            let steams = steam::installations(&home);
            let game = Some(steam::find_game(&steams));
            let rom = me.state.lock().unwrap().config.rom.clone();
            let verdict = rom.as_deref().map(rom::verify);
            let suggestion = if verdict.as_ref().is_some_and(Verdict::ok) { None } else { rom::find_candidate(&home) };
            let sandbox = Some(system::sandbox());
            {
                let mut state = me.state.lock().unwrap();
                state.found = Found { checked: true, steams, game, verdict, suggestion, sandbox };
                state.record = SetupRecord::load(&state.root);
            }
            me.with_app(|app, c| c.show_checks(app));
            let root = me.state.lock().unwrap().root.clone();
            let size = system::size_of(&root);
            me.with_app(move |app, _| {
                if size > 0 {
                    app.set_data_detail(format!("{} in {}", system::human_size(size), paths::pretty(&root)).into());
                }
            });
        });
    }

    fn setup_ready(state: &State) -> bool {
        match (&state.bundle, &state.found.game, &state.config.rom) {
            (Some(bundle), Some(GameStatus::Ready(game)), Some(rom)) => state.record.ready_for(&bundle.id(), &game.dir, rom),
            _ => false,
        }
    }

    fn show_checks(&self, app: &AppWindow) {
        let mut state = self.state.lock().unwrap();
        let (rows, ready) = rows(&state);
        app.set_checks_blocked(rows.iter().any(|r| r.tone == Tone::Error));
        app.set_checks(model(rows));
        app.set_checks_ready(ready);
        app.set_checking(false);
        let setup_ready = Controller::setup_ready(&state);
        app.set_setup_done(setup_ready);
        if let Some(GameStatus::Ready(game)) = &state.found.game {
            let build = game.build.clone().unwrap_or_default();
            app.set_game_detail(if build == steam::TESTED_BUILD || build.is_empty() { "Dead Cells v35".into() } else { format!("Dead Cells build {build}").into() });
            let tested = if build == steam::TESTED_BUILD { " (tested)" } else { "" };
            app.set_game_path(format!("{} · build {build}{tested}\n{}", game.steam.kind.label(), paths::pretty(&game.dir)).into());
        }
        if let Some(rom) = &state.config.rom {
            app.set_rom_name(file_name(rom).into());
            let verified = if state.found.verdict.as_ref().is_some_and(Verdict::ok) { "USA · revision 0 · verified" } else { "Not verified" };
            app.set_rom_detail(format!("{verified}\n{}", paths::pretty(rom.parent().unwrap_or(rom))).into());
        } else {
            app.set_rom_name("No ROM chosen".into());
            app.set_rom_detail("Choose your Kirby Super Star Ultra (USA) file".into());
        }
        let first = std::mem::replace(&mut state.first_check, false);
        drop(state);
        if ready && setup_ready {
            self.load_art(app);
            if first || app.get_screen() == Screen::Checks {
                app.set_screen(Screen::Home);
            }
        } else if app.get_screen() == Screen::Home {
            app.set_screen(Screen::Checks);
        }
    }

    fn check_action(&self, id: &str, which: &str) {
        let suggestion = self.state.lock().unwrap().found.suggestion.clone();
        match (id, which) {
            ("bundle", _) => drop(system::open(update::REPOSITORY)),
            ("steam", _) => drop(system::open("https://store.steampowered.com/about/")),
            ("game", _) => {
                let installed = matches!(self.state.lock().unwrap().found.game, Some(GameStatus::Windows(_) | GameStatus::Incomplete(_)));
                let target = if installed { format!("steam://nav/games/details/{}", steam::APP_ID) } else { format!("steam://install/{}", steam::APP_ID) };
                let _ = system::open(&target);
            }
            ("rom", "primary") if suggestion.is_some() && self.state.lock().unwrap().rejected.is_none() => {
                self.use_rom(suggestion.unwrap());
            }
            ("rom", _) => self.pick_rom(),
            ("bwrap", "primary") => drop(system::open(&format!("{HELP}#bubblewrap"))),
            ("bwrap", "copy") => {}
            _ => {}
        }
    }

    fn pick_rom(&self) {
        let me = self.clone();
        std::thread::spawn(move || {
            let start = me.state.lock().unwrap().config.rom.as_ref().and_then(|p| p.parent().map(Path::to_path_buf)).unwrap_or_else(|| paths::home().join("Documents"));
            let picked = rfd::FileDialog::new()
                .set_title("Choose your Kirby Super Star Ultra (USA) ROM")
                .add_filter("Nintendo DS game", &["nds", "NDS"])
                .set_directory(if start.is_dir() { start } else { paths::home() })
                .pick_file();
            if let Some(path) = picked {
                me.use_rom(path);
            }
        });
    }

    /// Checks a ROM the player chose and remembers it when it is the right one.
    fn use_rom(&self, path: PathBuf) {
        let me = self.clone();
        std::thread::spawn(move || {
            me.with_app(|app, _| app.set_checking(true));
            let verdict = rom::verify(&path);
            {
                let mut state = me.state.lock().unwrap();
                if verdict.ok() {
                    state.config.rom = Some(path.clone());
                    state.found.verdict = Some(verdict);
                    state.found.suggestion = None;
                    state.rejected = None;
                    let _ = state.config.save(&state.config_dir);
                } else {
                    state.rejected = Some((path, verdict));
                }
            }
            me.with_app(|app, c| {
                let on_home = app.get_screen() == Screen::Home;
                c.show_checks(app);
                // A new ROM after setup only needs the mod staged again.
                let state = c.state.lock().unwrap();
                let needs_setup = !Controller::setup_ready(&state) && state.record.finished && state.rejected.is_none();
                drop(state);
                if on_home && needs_setup {
                    c.start_setup(false);
                }
            });
        });
    }

    fn next(&self) {
        let ready = Controller::setup_ready(&self.state.lock().unwrap());
        if ready {
            self.with_app(|app, c| {
                c.load_art(app);
                app.set_screen(Screen::Home);
            });
        } else {
            self.start_setup(false);
        }
    }

    // Setup

    fn start_setup(&self, repair: bool) {
        let mut state = self.state.lock().unwrap();
        if state.setup.is_some() {
            return;
        }
        let (Some(bundle), Some(GameStatus::Ready(game)), Some(rom)) = (state.bundle.clone(), state.found.game.clone(), state.config.rom.clone()) else {
            drop(state);
            self.with_app(|app, _| app.set_screen(Screen::Checks));
            return;
        };
        let control = Control::default();
        state.setup = Some(control.clone());
        state.setup_steps = setup::TITLES.iter().map(|t| StepItem { title: (*t).into(), tone: Tone::Pending, ..Default::default() }).collect();
        state.log.clear();
        let steps = state.setup_steps.clone();
        let inputs = setup::Inputs { bundle, root: state.root.clone(), rom, game: game.dir.clone(), repair };
        drop(state);
        self.with_app(move |app, _| {
            app.set_steps(model(steps));
            app.set_current_step(0);
            app.set_setup_progress(0.0);
            app.set_setup_state(SetupState::Running);
            app.set_setup_remaining("starting".into());
            app.set_setup_error(SharedString::new());
            app.set_setup_log(SharedString::new());
            app.set_settings_open(false);
            app.set_screen(Screen::Setup);
        });
        let me = self.clone();
        let emit: Arc<dyn Fn(setup::Event) + Send + Sync> = Arc::new(move |event| me.setup_event(event));
        std::thread::spawn(move || setup::run(inputs, control, emit));
    }

    fn setup_event(&self, event: setup::Event) {
        let mut state = self.state.lock().unwrap();
        match event {
            setup::Event::Log(line) => {
                state.log.push_back(line);
                while state.log.len() > 400 {
                    state.log.pop_front();
                }
                let text: Vec<String> = state.log.iter().rev().take(120).rev().cloned().collect();
                drop(state);
                self.with_app(move |app, _| app.set_setup_log(text.join("\n").into()));
            }
            setup::Event::Step { index, state: step, detail, elapsed } => {
                let Some(item) = state.setup_steps.get_mut(index) else { return };
                if step == setup::State::Busy && matches!(item.tone, Tone::Ok) {
                    return;
                }
                item.tone = match step {
                    setup::State::Busy => Tone::Busy,
                    setup::State::Done | setup::State::Kept => Tone::Ok,
                };
                item.detail = if step == setup::State::Busy { detail.into() } else { SharedString::new() };
                item.elapsed = match (step, elapsed) {
                    (setup::State::Kept, _) => "kept".into(),
                    (_, Some(elapsed)) => minutes(elapsed).into(),
                    _ => SharedString::new(),
                };
                let steps = state.setup_steps.clone();
                drop(state);
                let current = if step == setup::State::Busy { index as i32 } else { -1 };
                self.with_app(move |app, _| {
                    app.set_steps(model(steps));
                    if current >= 0 {
                        app.set_current_step(current);
                    }
                });
            }
            setup::Event::Progress { overall, remaining: left } => {
                drop(state);
                self.with_app(move |app, _| {
                    app.set_setup_progress(overall);
                    app.set_setup_remaining(remaining(left).into());
                });
            }
            setup::Event::Failed { index, message } => {
                state.setup = None;
                if let Some(item) = state.setup_steps.get_mut(index) {
                    item.tone = Tone::Error;
                    item.detail = "Stopped".into();
                }
                let steps = state.setup_steps.clone();
                drop(state);
                self.with_app(move |app, _| {
                    app.set_steps(model(steps));
                    app.set_current_step(index as i32);
                    app.set_setup_error(message.into());
                    app.set_setup_state(SetupState::Failed);
                });
            }
            setup::Event::Cancelled => {
                state.setup = None;
                for item in state.setup_steps.iter_mut().filter(|i| i.tone == Tone::Busy) {
                    item.tone = Tone::Pending;
                    item.detail = SharedString::new();
                }
                let steps = state.setup_steps.clone();
                drop(state);
                self.with_app(move |app, _| {
                    app.set_steps(model(steps));
                    app.set_current_step(-1);
                    app.set_setup_state(SetupState::Cancelled);
                });
            }
            setup::Event::Finished => {
                state.setup = None;
                state.record = SetupRecord::load(&state.root);
                drop(state);
                self.with_app(|app, c| {
                    c.load_art(app);
                    app.set_setup_done(true);
                    app.set_screen(Screen::Home);
                    app.invoke_show_toast("Kirby is ready!".into());
                });
            }
        }
    }

    fn cancel_setup(&self) {
        if let Some(control) = self.state.lock().unwrap().setup.clone() {
            control.cancel();
        }
    }

    // Home

    fn load_art(&self, app: &AppWindow) {
        let root = self.state.lock().unwrap().root.clone();
        let frames = root.join("rom/decoded/bkir/frames");
        match (Image::load_from_path(&frames.join("0041.png")), Image::load_from_path(&frames.join("0009.png"))) {
            (Ok(kirby), Ok(happy)) => {
                app.set_kirby(kirby);
                app.set_kirby_happy(happy);
                app.set_has_kirby(true);
            }
            _ => app.set_has_kirby(false),
        }
        let icons: Vec<Ability> = ABILITIES
            .iter()
            .filter_map(|name| {
                let icon = Image::load_from_path(&root.join(format!("ability-icons/{name}-32.png"))).ok()?;
                Some(Ability { name: (*name).into(), icon })
            })
            .collect();
        app.set_abilities(model(if icons.len() == ABILITIES.len() { icons } else { Vec::new() }));
    }

    fn play(&self, confirmed: bool) {
        let Some(app) = self.weak.upgrade() else { return };
        if app.get_play_state() != PlayState::Idle {
            return;
        }
        if !confirmed {
            if let Some(free) = system::memory_available().filter(|free| *free < MEMORY_WANTED) {
                self.ask(
                    "memory",
                    "Your computer is low on memory",
                    &format!("Dead Cells with Kirby needs about 6 GB of free memory, and {:.1} GB is free now. Close other programs for a smoother game, or play anyway.", (free as f64 / (1u64 << 30) as f64 * 10.0).floor() / 10.0),
                    "Play anyway",
                    "Not now",
                    true,
                );
                return;
            }
        }
        let state = self.state.lock().unwrap();
        let Some(GameStatus::Ready(game)) = state.found.game.clone() else { return };
        let (root, smoke) = (state.root.clone(), state.smoke_seconds);
        drop(state);
        app.set_play_state(if steam::running() { PlayState::Launching } else { PlayState::StartingSteam });
        let me = self.clone();
        std::thread::spawn(move || {
            if !steam::running() {
                if steam::start(game.steam.kind).is_err() || !wait_for_steam(Duration::from_secs(90)) {
                    me.with_app(|app, c| {
                        app.set_play_state(PlayState::Idle);
                        c.ask("steam", "Steam didn't open", "Open Steam yourself and sign in, then press Play again.", "OK", "", false);
                    });
                    return;
                }
                // Give a freshly started Steam a moment to sign in.
                std::thread::sleep(Duration::from_secs(8));
            }
            me.with_app(|app, _| app.set_play_state(PlayState::Launching));
            let log = root.join("logs/play.log");
            let launch = play::Launch {
                python: &setup::python(&root),
                project: &setup::project(&root),
                root: &root,
                steam_root: &game.steam.root,
                log: &log,
                smoke_seconds: smoke,
            };
            let events = me.clone();
            let started = play::start(launch, move |event| events.play_event(event));
            match started {
                Ok(pid) => me.state.lock().unwrap().game_pid = Some(pid),
                Err(error) => me.with_app(move |app, c| {
                    app.set_play_state(PlayState::Idle);
                    c.ask("crash", "The game couldn't start", &format!("{error}. Try Repair setup in Settings."), "OK", "", true);
                }),
            }
        });
    }

    fn play_event(&self, event: play::Event) {
        match event {
            play::Event::Opening => self.with_app(|app, _| app.set_play_state(PlayState::Running)),
            play::Event::RuntimeLog(path) => self.state.lock().unwrap().last_game_log = Some(path),
            play::Event::Exited { success, last_lines } => {
                {
                    let mut state = self.state.lock().unwrap();
                    state.game_pid = None;
                    state.log = last_lines.iter().cloned().collect();
                }
                let reason = play::explain(&last_lines);
                self.with_app(move |app, c| {
                    let was_stopping = app.get_play_state() == PlayState::Stopping;
                    app.set_play_state(PlayState::Idle);
                    if !success && !was_stopping {
                        c.ask("crash", "The game closed with a problem", &reason, "Open the log", "Close", true);
                    }
                });
            }
        }
    }

    fn stop_game(&self) {
        if let Some(pid) = self.state.lock().unwrap().game_pid {
            play::stop(pid);
            self.with_app(|app, _| app.set_play_state(PlayState::Stopping));
        }
    }

    // Settings and questions

    fn settings_action(&self, which: &str) {
        let state = self.state.lock().unwrap();
        let root = state.root.clone();
        let game_log = state.last_game_log.clone();
        drop(state);
        match which {
            "change-rom" => self.pick_rom(),
            "open-saves" => drop(system::open_path(&root.join("lab/save"))),
            "open-backups" => drop(system::open_path(&root.join("save-backups"))),
            "open-log" => {
                let log = game_log.filter(|p| p.is_file()).unwrap_or_else(|| root.join("logs/play.log"));
                let _ = if log.is_file() { system::open(&log.to_string_lossy()) } else { system::open_path(&root.join("logs")) };
            }
            "repair" => self.ask("repair", "Repair the setup?", "This builds everything again from your ROM and game. It takes a few minutes. Kirby's saves are kept.", "Repair", "Cancel", false),
            "open-data" => drop(system::open_path(&root)),
            "update" => {
                let url = self.state.lock().unwrap().update.as_ref().map(|r| r.url.clone());
                match url {
                    Some(url) => drop(system::open(&url)),
                    None => self.check_update(true),
                }
            }
            "licenses" => self.with_app(|app, _| app.set_licenses_open(true)),
            "source" => drop(system::open(update::REPOSITORY)),
            _ => {}
        }
    }

    fn check_update(&self, by_hand: bool) {
        self.with_app(|app, _| app.set_update_busy(true));
        let me = self.clone();
        std::thread::spawn(move || {
            let result = update::newer_than(env!("CARGO_PKG_VERSION"));
            me.with_app(move |app, c| {
                app.set_update_busy(false);
                match result {
                    Ok(Some(release)) => {
                        app.set_update_version(release.version.clone().into());
                        app.set_update_detail(format!("Version {} is available", release.version).into());
                        app.set_update_action("Get it".into());
                        c.state.lock().unwrap().update = Some(release);
                    }
                    Ok(None) => {
                        app.set_update_detail("You have the latest version".into());
                        if by_hand {
                            app.invoke_show_toast("You have the latest version".into());
                        }
                    }
                    Err(_) => {
                        app.set_update_detail("Couldn't reach GitHub to check".into());
                        if by_hand {
                            app.invoke_show_toast("Couldn't reach GitHub".into());
                        }
                    }
                }
            });
        });
    }

    fn ask(&self, id: &str, title: &str, body: &str, confirm: &str, cancel: &str, warning: bool) {
        let (id, title, body, confirm, cancel) = (id.to_string(), title.to_string(), body.to_string(), confirm.to_string(), cancel.to_string());
        self.with_app(move |app, _| {
            app.set_question_title(title.into());
            app.set_question_body(body.into());
            app.set_question_confirm(confirm.into());
            app.set_question_cancel(cancel.into());
            app.set_question_warning(warning);
            app.set_question_id(id.into());
        });
    }

    fn answer(&self, id: &str, yes: bool) {
        self.with_app(|app, _| app.set_question_id(SharedString::new()));
        match (id, yes) {
            ("memory", true) => self.play(true),
            ("repair", true) => self.start_setup(true),
            ("crash", true) => self.settings_action("open-log"),
            _ => {}
        }
    }
}

/// `--check`: the same checks as the window, printed. Returns whether play is possible.
pub fn print_checks(bundle: Option<PathBuf>) -> bool {
    let root = paths::data_root();
    let config_dir = paths::config_dir();
    let config = Config::load(&config_dir);
    let home = paths::home();
    let steams = steam::installations(&home);
    let game = Some(steam::find_game(&steams));
    let verdict = config.rom.as_deref().map(rom::verify);
    let suggestion = if verdict.as_ref().is_some_and(Verdict::ok) { None } else { rom::find_candidate(&home) };
    let state = State {
        record: SetupRecord::load(&root),
        bundle: paths::find_bundle(bundle),
        found: Found { checked: true, steams, game, verdict, suggestion, sandbox: Some(system::sandbox()) },
        root,
        config_dir,
        config,
        rejected: None,
        setup: None,
        setup_steps: Vec::new(),
        log: VecDeque::new(),
        game_pid: None,
        last_game_log: None,
        update: None,
        smoke_seconds: None,
        first_check: true,
    };
    let (rows, ready) = rows(&state);
    for row in rows {
        let mark = match row.tone {
            Tone::Ok => "ok  ",
            Tone::Warn => "warn",
            Tone::Error => "fix ",
            _ => "todo",
        };
        println!("[{mark}] {}: {}", row.title, row.detail);
        if !row.command.is_empty() {
            println!("       {}", row.command);
        }
    }
    if let Some(GameStatus::Ready(game)) = &state.found.game {
        println!("Dead Cells: {} ({})", game.dir.display(), game.steam.kind.label());
    }
    if let Some(bundle) = &state.bundle {
        println!("Mod files: {} ({})", bundle.root.display(), bundle.id());
    }
    println!("Data root: {}", state.root.display());
    println!("Setup: {}", if Controller::setup_ready(&state) { "done" } else { "needed" });
    ready
}

fn wait_for_steam(limit: Duration) -> bool {
    let deadline = std::time::Instant::now() + limit;
    while std::time::Instant::now() < deadline {
        if steam::running() {
            return true;
        }
        std::thread::sleep(Duration::from_secs(1));
    }
    false
}

#[cfg(test)]
mod tests {
    use super::*;

    fn state() -> State {
        State {
            root: "/data".into(),
            config_dir: "/config".into(),
            config: Config::default(),
            bundle: Some(Bundle { root: "/bundle".into(), version: "0.2.0".into(), commit: "abc".into() }),
            found: Found {
                checked: true,
                steams: vec![Steam { root: "/steam".into(), kind: steam::Kind::Native }],
                game: Some(GameStatus::Ready(steam::Game {
                    dir: "/steam/steamapps/common/Dead Cells".into(),
                    build: Some(steam::TESTED_BUILD.into()),
                    steam: Steam { root: "/steam".into(), kind: steam::Kind::Native },
                })),
                verdict: None,
                suggestion: None,
                sandbox: Some(Sandbox::Ready),
            },
            rejected: None,
            record: SetupRecord::default(),
            setup: None,
            setup_steps: Vec::new(),
            log: VecDeque::new(),
            game_pid: None,
            last_game_log: None,
            update: None,
            smoke_seconds: None,
            first_check: true,
        }
    }

    #[test]
    fn nothing_is_reported_missing_before_the_first_look() {
        let mut state = state();
        state.found = Found::default();
        let (rows, ready) = rows(&state);
        assert!(!ready);
        assert!(rows.iter().all(|r| r.tone == Tone::Busy && r.action.is_empty()));
    }

    fn tone(rows: &[CheckItem], id: &str) -> Tone {
        rows.iter().find(|r| r.id == id).unwrap().tone
    }

    #[test]
    fn a_rom_is_needed_before_setup() {
        let mut s = state();
        let (rows, ready) = rows(&s);
        assert!(!ready);
        assert_eq!(tone(&rows, "rom"), Tone::Pending);
        assert_eq!(rows.iter().find(|r| r.id == "rom").unwrap().action, "Choose file");
        s.found.suggestion = Some(paths::home().join("Documents/ROMs/kssu.nds"));
        let (rows, _) = super::rows(&s);
        let rom = rows.iter().find(|r| r.id == "rom").unwrap();
        assert_eq!(rom.action, "Use this file");
        assert!(rom.detail.contains("in Documents"));
        s.config.rom = Some("/roms/kssu.nds".into());
        s.found.verdict = Some(Verdict::Verified);
        s.found.suggestion = None;
        let (rows, ready) = super::rows(&s);
        assert!(ready);
        assert_eq!(tone(&rows, "rom"), Tone::Ok);
    }

    #[test]
    fn problems_block_play_and_say_what_to_do() {
        let mut s = state();
        s.config.rom = Some("/roms/kssu.nds".into());
        s.found.verdict = Some(Verdict::Verified);
        s.rejected = Some(("/roms/europe.nds".into(), Verdict::Region("European")));
        let (rows, ready) = rows(&s);
        assert!(!ready);
        assert!(rows.iter().find(|r| r.id == "rom").unwrap().detail.contains("European version"));
        s.rejected = None;
        s.found.sandbox = Some(Sandbox::Missing);
        let (rows, ready) = super::rows(&s);
        assert!(!ready);
        assert_eq!(tone(&rows, "bwrap"), Tone::Error);
        s.found.sandbox = Some(Sandbox::Ready);
        s.found.steams.clear();
        s.found.game = Some(GameStatus::Missing);
        let (rows, ready) = super::rows(&s);
        assert!(!ready);
        assert_eq!(tone(&rows, "steam"), Tone::Error);
        assert_eq!(tone(&rows, "game"), Tone::Pending);
    }

    #[test]
    fn a_newer_game_build_is_a_warning_only() {
        let mut s = state();
        s.config.rom = Some("/roms/kssu.nds".into());
        s.found.verdict = Some(Verdict::Verified);
        if let Some(GameStatus::Ready(game)) = &mut s.found.game {
            game.build = Some("99999999".into());
        }
        let (rows, ready) = rows(&s);
        assert!(ready);
        assert_eq!(tone(&rows, "game"), Tone::Warn);
    }

    #[test]
    fn time_left_reads_naturally() {
        assert_eq!(remaining(None), "starting");
        assert_eq!(remaining(Some(Duration::from_secs(30))), "less than a minute left");
        assert_eq!(remaining(Some(Duration::from_secs(400))), "about 7 min left");
        assert_eq!(minutes(Duration::from_secs(72)), "1:12");
    }
}
