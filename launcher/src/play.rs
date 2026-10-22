//! Starts the game through the mod's own launch script, which backs up the
//! real saves, keeps the installed game read-only and waits until it closes.

use std::io::{BufRead, BufReader, Write};
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
    /// Windows: play with the player's normal rights, after they agreed to it
    /// because the low-integrity sandbox could not be made (#250).
    pub normal_integrity: bool,
}

/// The running launch script, to ask it to close the game.
pub struct Running {
    #[cfg(unix)]
    pid: u32,
    /// Windows has no Ctrl+C for a program without a console of its own, so
    /// the script reads `stop` here instead.
    #[cfg(windows)]
    input: std::process::ChildStdin,
}

impl Running {
    /// Asks the launch script to close the game; it stops the game and checks the saves.
    pub fn stop(&mut self) {
        #[cfg(unix)]
        unsafe {
            libc::kill(self.pid as i32, libc::SIGINT);
        }
        #[cfg(windows)]
        {
            let _ = writeln!(self.input, "stop");
            let _ = self.input.flush();
        }
    }
}

/// Lines from the launch script that mean Windows refused the low-integrity sandbox.
pub fn sandbox_refused(lines: &[String]) -> bool {
    lines.iter().any(|line| line.starts_with("Windows refused to label the lab") || line.starts_with("Windows refused to start the game"))
}

pub fn start(launch: Launch, emit: impl Fn(Event) + Send + 'static) -> std::io::Result<Running> {
    let mut command = crate::setup::python_command(launch.python, launch.project, launch.root, None);
    command.arg("scripts/launch_runtime.py").arg("--steam-root").arg(launch.steam_root);
    match launch.smoke_seconds {
        // The documented movement check: a new run as Kirby, walking and jumping.
        Some(seconds) => command.args(["--seconds", &seconds.to_string(), "--start-game", "--outfit", "KirbySuperStarUltra", "--input-plan", "movement"]),
        None => command.arg("--play"),
    };
    if launch.normal_integrity {
        command.arg("--normal-integrity");
    }
    let (reader, writer) = std::io::pipe()?;
    command.stdin(if cfg!(windows) { Stdio::piped() } else { Stdio::null() }).stdout(writer.try_clone()?).stderr(writer);
    let mut child: Child = crate::system::detach(&mut command).spawn()?;
    drop(command);
    #[cfg(unix)]
    let running = Running { pid: child.id() };
    #[cfg(windows)]
    let running = Running { input: child.stdin.take().expect("the launch script's input is a pipe") };
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
    Ok(running)
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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_refused_sandbox_is_told_apart() {
        let lines = |text: &str| text.lines().map(str::to_string).collect::<Vec<_>>();
        assert!(sandbox_refused(&lines("Saves backed up to x\nWindows refused to label the lab low integrity (Access denied). With --normal-integrity the game runs with only the save backup and check.")));
        assert!(sandbox_refused(&lines("Windows refused to start the game ([WinError 5] Access denied).")));
        assert!(!sandbox_refused(&lines("The game exited with an error; inspect the smoke log.")));
    }
}
