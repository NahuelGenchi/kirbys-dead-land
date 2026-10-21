//! Hidden `--autopilot DIR`, for checks: in the real window, it presses what a
//! player would (use the ROM the launcher found, set up, play), saves a snapshot
//! of the window at each change, and closes the launcher once the game closes.
//! It stops at any question or failure instead of answering for the player.

use crate::{AppWindow, PlayState, Screen, SetupState, Tone};
use slint::{ComponentHandle, Model, Timer, TimerMode};
use std::cell::{Cell, RefCell};
use std::path::PathBuf;
use std::rc::Rc;
use std::time::{Duration, Instant};

pub struct Autopilot {
    _timer: Timer,
    /// The exit code: 0 once the game ran and closed, 2 when it stopped early.
    pub code: Rc<Cell<i32>>,
}

fn describe(app: &AppWindow) -> String {
    let step = app.get_steps().row_data(app.get_current_step().max(0) as usize).map(|s| s.title.to_string()).unwrap_or_default();
    let rows: Vec<String> = app.get_checks().iter().map(|r| format!("{}={:?}", r.id, r.tone)).collect();
    match app.get_screen() {
        Screen::Checks => format!("checks checking={} ready={} {}", app.get_checking(), app.get_checks_ready(), rows.join(" ")),
        Screen::Setup => format!("setup {:?} step {}: {step}", app.get_setup_state(), app.get_current_step() + 1),
        Screen::Home => format!("home play={:?} kirby={} abilities={}", app.get_play_state(), app.get_has_kirby(), app.get_abilities().row_count()),
    }
}

pub fn start(app: &AppWindow, dir: PathBuf) -> Autopilot {
    let _ = std::fs::create_dir_all(&dir);
    let code = Rc::new(Cell::new(1));
    let (started, last, shots) = (Instant::now(), RefCell::new(String::new()), Cell::new(0));
    let (used_rom, pressed_next, played, running) = (Cell::new(false), Cell::new(false), Cell::new(false), Cell::new(false));
    let weak = app.as_weak();
    let result = code.clone();
    let timer = Timer::default();
    timer.start(TimerMode::Repeated, Duration::from_millis(1000), move || {
        let Some(app) = weak.upgrade() else { return };
        let say = |text: &str| println!("[{:>5.0}s] {text}", started.elapsed().as_secs_f32());
        let snapshot = |label: &str| {
            shots.set(shots.get() + 1);
            let path = dir.join(format!("{:02}-{label}.png", shots.get()));
            if let Ok(buffer) = app.window().take_snapshot() {
                let _ = crate::shots::write_png(&path, buffer.width(), buffer.height(), buffer.as_bytes());
            }
        };
        let stop = |code: i32| {
            result.set(code);
            let _ = slint::quit_event_loop();
        };
        let now = describe(&app);
        if now != *last.borrow() {
            say(&now);
            let label = now.split_whitespace().next().unwrap_or("window").to_string();
            snapshot(&label);
            *last.borrow_mut() = now;
        }
        if !app.get_question_id().is_empty() {
            say(&format!("question \"{}\": {}", app.get_question_title(), app.get_question_body()));
            snapshot("question");
            return stop(2);
        }
        if app.get_checking() {
            return;
        }
        match app.get_screen() {
            Screen::Checks => {
                let rows: Vec<_> = app.get_checks().iter().collect();
                if let Some(problem) = rows.iter().find(|r| r.tone == Tone::Error) {
                    say(&format!("blocked by {}: {}", problem.title, problem.detail));
                    return stop(2);
                }
                let rom = rows.iter().find(|r| r.id == "rom");
                if rom.is_some_and(|r| r.tone == Tone::Pending && r.action == "Use this file") && !used_rom.replace(true) {
                    say("pressing \"Use this file\"");
                    app.invoke_check_action("rom".into(), "primary".into());
                } else if app.get_checks_ready() && !pressed_next.replace(true) {
                    say("pressing the main button");
                    app.invoke_checks_next();
                }
            }
            Screen::Setup => {
                if matches!(app.get_setup_state(), SetupState::Failed | SetupState::Cancelled) {
                    say(&format!("setup stopped: {}", app.get_setup_error()));
                    return stop(2);
                }
            }
            Screen::Home => match app.get_play_state() {
                PlayState::Idle if !played.replace(true) => {
                    let steps: Vec<String> = app.get_steps().iter().map(|s| format!("{} {}", s.title, s.elapsed)).collect();
                    if !steps.is_empty() {
                        say(&format!("setup steps: {}", steps.join(", ")));
                    }
                    say("pressing Play");
                    app.invoke_play();
                }
                PlayState::Idle => {
                    say(if running.get() { "the game closed" } else { "the game never opened" });
                    stop(if running.get() { 0 } else { 2 });
                }
                PlayState::Running => running.set(true),
                _ => {}
            },
        }
    });
    Autopilot { _timer: timer, code }
}
