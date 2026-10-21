//! Starts the game through the mod's own launch script, which backs up the
//! real saves, keeps the installed game read-only and waits until it closes.

use std::io::{BufRead, BufReader, Write};
use std::os::unix::process::CommandExt;
use std::path::{Path, PathBuf};
use std::process::{Child, Stdio};

pub enum Event {
    /// The game window is opening.
    Opening,
    /// Where this run's game log is.
    RuntimeLog(PathBuf),
    Exited { success: bool, last_lines: Vec<String> },
}

pub struct Launch<'a> {
    pub python: &'a Path,
    pub project: &'a Path,
    pub root: &'a Path,
    pub steam_root: &'a Path,
    pub log: &'a Path,
    /// For checks only: a bounded offscreen run with automated input instead of play.
    pub smoke_seconds: Option<u32>,
}

pub fn start(launch: Launch, emit: impl Fn(Event) + Send + 'static) -> std::io::Result<u32> {
    let mut command = crate::setup::python_command(launch.python, launch.project, launch.root, None);
    command.arg("scripts/launch_runtime.py").arg("--steam-root").arg(launch.steam_root);
    match launch.smoke_seconds {
        // The documented movement check: a new run as Kirby, walking and jumping.
        Some(seconds) => command.args(["--seconds", &seconds.to_string(), "--start-game", "--outfit", "KirbySuperStarUltra", "--input-plan", "movement"]),
        None => command.arg("--play"),
    };
    let (reader, writer) = std::io::pipe()?;
    command.stdin(Stdio::null()).stdout(writer.try_clone()?).stderr(writer).process_group(0);
    let mut child: Child = command.spawn()?;
    drop(command);
    let pid = child.id();
    if let Some(dir) = launch.log.parent() {
        std::fs::create_dir_all(dir)?;
    }
    let mut log = std::fs::File::create(launch.log)?;
    let smoke = launch.smoke_seconds.is_some();
    std::thread::spawn(move || {
        let mut tail: Vec<String> = Vec::new();
        for line in BufReader::new(reader).lines().map_while(Result::ok) {
            let _ = writeln!(log, "{line}");
            // An offscreen check prints no greeting; its saves backup comes just before the game starts.
            if line.starts_with("Opening Kirby in Dead Cells") || (smoke && line.starts_with("Saves backed up to")) {
                emit(Event::Opening);
            }
            if let Some(path) = line.strip_prefix("Runtime log: ") {
                emit(Event::RuntimeLog(PathBuf::from(path.trim())));
            }
            tail.push(line);
            if tail.len() > 40 {
                tail.remove(0);
            }
        }
        let success = child.wait().map(|status| status.success()).unwrap_or(false);
        emit(Event::Exited { success, last_lines: tail });
    });
    Ok(pid)
}

/// Asks the launch script to close the game; it stops the game and checks the saves.
pub fn stop(pid: u32) {
    unsafe {
        libc::kill(pid as i32, libc::SIGINT);
    }
}

/// The most useful line to show when the game closed with a problem.
pub fn explain(lines: &[String]) -> String {
    lines
        .iter()
        .rev()
        .find(|line| {
            let line = line.trim();
            !line.is_empty() && !line.starts_with('{') && !line.starts_with('}') && !line.starts_with('"') && !line.starts_with("Traceback")
        })
        .map(|line| line.trim().to_string())
        .unwrap_or_else(|| "The game stopped without saying why.".into())
}
