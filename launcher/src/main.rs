//! Kirby's Dead Land launcher: checks the player's games, sets the mod up once
//! from their own files, and starts it.

// A window, with no console of its own, on Windows (#250).
#![cfg_attr(windows, windows_subsystem = "windows")]

mod art;
mod autopilot;
mod config;
mod controller;
mod gamepad;
mod paths;
mod play;
mod rom;
mod setup;
mod shots;
mod steam;
mod system;
mod update;
#[cfg(windows)]
mod windows;

use art::Painter;
use slint::ComponentHandle;
use std::path::PathBuf;
use std::rc::Rc;

slint::include_modules!();

/// Settings › Licenses: the launcher's notices, then the bridge's and the runner's.
pub const NOTICES: &str = concat!(include_str!("../NOTICES.txt"), "\n\n\n", include_str!("../../packaging/NOTICES-emulator.txt"));

thread_local! {
    pub static ART: Rc<Painter> = Rc::new(Painter::new());
}

/// Hands the generated artwork to the UI, at the window's pixel density.
pub fn connect_art(app: &AppWindow) {
    let art = ART.with(|art| art.clone());
    let weak = app.as_weak();
    let scale = move || weak.upgrade().map(|app| app.window().scale_factor()).unwrap_or(1.0);
    let theme = app.global::<Art>();
    let (a, s) = (art.clone(), scale.clone());
    theme.on_glow(move |w, h, r, blur, color| {
        a.scale.set(s());
        a.glow(w, h, r, blur, color)
    });
    let (a, s) = (art.clone(), scale.clone());
    theme.on_pill(move |w, h, r, from, to| {
        a.scale.set(s());
        a.pill(w, h, r, from, to)
    });
    let (a, s) = (art.clone(), scale.clone());
    theme.on_ring(move |size, t, p, from, to, track| {
        a.scale.set(s());
        a.ring(size, t, p, from, to, track)
    });
    let (a, s) = (art.clone(), scale.clone());
    theme.on_arc(move |size, t, start, color| {
        a.scale.set(s());
        a.arc(size, t, start, color)
    });
    theme.on_sky(move |w, h| {
        art.scale.set(scale());
        art.sky(w, h)
    });
    app.set_app_icon(art::app_icon(128).0);
}

fn value(args: &[String], flag: &str) -> Option<String> {
    args.iter().position(|a| a == flag).and_then(|i| args.get(i + 1)).cloned()
}

const USAGE: &str = "Usage: kirbys-dead-land [--bundle DIR]
       kirbys-dead-land --check [--bundle DIR]
       kirbys-dead-land --screenshot DIR [--art-from DATA_ROOT] [--scale 1-4]
       kirbys-dead-land --write-icon FILE.png
       kirbys-dead-land --version";

fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    // A player starts it without options; anything else came from a terminal,
    // which should see what it prints.
    #[cfg(windows)]
    if !args.is_empty() {
        windows::attach_parent_console();
    }
    if args.iter().any(|a| a == "--help" || a == "-h") {
        println!("{USAGE}");
        return;
    }
    if args.iter().any(|a| a == "--version") {
        println!("kirbys-dead-land {}", env!("CARGO_PKG_VERSION"));
        return;
    }
    if args.iter().any(|a| a == "--check") {
        let ready = controller::print_checks(value(&args, "--bundle").map(PathBuf::from));
        std::process::exit(if ready { 0 } else { 1 });
    }
    if let Some(path) = value(&args, "--write-icon") {
        let (_, rgba) = art::app_icon(256);
        if let Err(error) = shots::write_png(std::path::Path::new(&path), 256, 256, &rgba) {
            eprintln!("{error}");
            std::process::exit(1);
        }
        return;
    }
    if let Some(out) = value(&args, "--screenshot") {
        let scale = match value(&args, "--scale").map(|s| s.parse::<f32>()) {
            None => 1.0,
            Some(Ok(scale)) if (1.0..=4.0).contains(&scale) => scale,
            Some(_) => {
                eprintln!("--scale takes a number from 1 to 4");
                std::process::exit(2);
            }
        };
        if let Err(error) = shots::run(&PathBuf::from(out), value(&args, "--art-from").map(PathBuf::from), scale) {
            eprintln!("{error}");
            std::process::exit(1);
        }
        return;
    }
    let options = controller::Options {
        bundle: value(&args, "--bundle").map(PathBuf::from),
        // Hidden: Play runs a bounded offscreen game with automated input, for checks.
        smoke_seconds: value(&args, "--smoke-play").and_then(|s| s.parse().ok()),
    };
    if cfg!(windows) && options.smoke_seconds.is_some() {
        eprintln!("--smoke-play runs on Linux only: offscreen tests run there (#246)");
        std::process::exit(2);
    }
    let app = match AppWindow::new() {
        Ok(app) => app,
        Err(error) => {
            eprintln!("The launcher window could not open: {error}");
            std::process::exit(1);
        }
    };
    connect_art(&app);
    let controller = controller::Controller::new(&app, options);
    // Hidden, for checks: drive the window as a player would and save snapshots.
    // `--yes ID` agrees to that one question, and `--close-after SECONDS` presses
    // Close once the game has run that long.
    let autopilot = value(&args, "--autopilot").map(|dir| {
        let choices = autopilot::Choices { yes: value(&args, "--yes"), close_after: value(&args, "--close-after").and_then(|s| s.parse().ok()) };
        autopilot::start(&app, PathBuf::from(dir), choices)
    });
    controller.start();
    gamepad::start(&app);
    if let Err(error) = app.run() {
        eprintln!("{error}");
        std::process::exit(1);
    }
    if let Some(autopilot) = autopilot {
        std::process::exit(autopilot.code.get());
    }
}

#[cfg(test)]
mod tests {
    #[test]
    fn notices_cover_the_locked_crates() {
        use sha2::Digest;
        let digest = sha2::Sha256::digest(include_bytes!("../Cargo.lock"));
        let lock: String = digest.iter().map(|b| format!("{b:02x}")).collect();
        assert!(crate::NOTICES.contains(&format!("from Cargo.lock {}.", &lock[..16])), "Cargo.lock changed: run tools/launcher_notices.py");
        assert!(crate::NOTICES.contains("notices for the emulator programs"));
    }
}
