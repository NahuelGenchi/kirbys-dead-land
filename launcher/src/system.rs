//! Small questions about the player's system, and opening things for them.

use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::time::{Duration, Instant};

pub fn which(name: &str) -> Option<PathBuf> {
    let path = std::env::var_os("PATH")?;
    std::env::split_paths(&path).map(|dir| dir.join(name)).find(|candidate| {
        std::fs::metadata(candidate).map(|m| m.is_file() && std::os::unix::fs::PermissionsExt::mode(&m.permissions()) & 0o111 != 0).unwrap_or(false)
    })
}

/// A command with none of the launcher's own runtime variables, so programs it
/// opens behave as if the player had started them.
pub fn command(program: impl AsRef<std::ffi::OsStr>) -> Command {
    let mut command = Command::new(program);
    for key in ["LD_LIBRARY_PATH", "LD_PRELOAD", "PYTHONHOME", "PYTHONPATH", "APPDIR", "APPIMAGE", "ARGV0", "OWD"] {
        command.env_remove(key);
    }
    command
}

pub fn spawn_detached(program: &str, args: &[&str]) -> std::io::Result<()> {
    use std::os::unix::process::CommandExt;
    let mut child = command(program)
        .args(args)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .process_group(0)
        .spawn()?;
    // Reap it when it exits, so it never lingers as a zombie.
    std::thread::spawn(move || child.wait());
    Ok(())
}

/// Opens a folder, file or web address with the player's usual program.
pub fn open(target: &str) -> std::io::Result<()> {
    spawn_detached("xdg-open", &[target])
}

pub fn open_path(path: &Path) -> std::io::Result<()> {
    if !path.exists() {
        std::fs::create_dir_all(path)?;
    }
    open(&path.to_string_lossy())
}

/// The distribution's ID and the IDs it derives from, from `/etc/os-release`.
pub fn distribution() -> Vec<String> {
    let text = std::fs::read_to_string("/etc/os-release").or_else(|_| std::fs::read_to_string("/usr/lib/os-release")).unwrap_or_default();
    parse_os_release(&text)
}

fn parse_os_release(text: &str) -> Vec<String> {
    let mut ids = Vec::new();
    for line in text.lines() {
        let Some((key, value)) = line.split_once('=') else { continue };
        if key == "ID" || key == "ID_LIKE" {
            ids.extend(value.trim_matches('"').split_whitespace().map(|s| s.to_lowercase()));
        }
    }
    ids
}

/// The command that installs a package on this distribution.
pub fn install_command(ids: &[String], package: &str) -> Option<String> {
    for id in ids {
        let command = match id.as_str() {
            "debian" | "ubuntu" | "linuxmint" | "pop" | "elementary" | "zorin" | "raspbian" => format!("sudo apt install {package}"),
            "fedora" | "rhel" | "centos" | "nobara" | "ultramarine" | "almalinux" | "rocky" => format!("sudo dnf install {package}"),
            "arch" | "manjaro" | "endeavouros" | "cachyos" | "garuda" | "steamos" => format!("sudo pacman -S {package}"),
            "opensuse" | "opensuse-tumbleweed" | "opensuse-leap" | "suse" => format!("sudo zypper install {package}"),
            "void" => format!("sudo xbps-install {package}"),
            "alpine" => format!("sudo apk add {package}"),
            "gentoo" => format!("sudo emerge sys-apps/{package}"),
            "solus" => format!("sudo eopkg install {package}"),
            _ => continue,
        };
        return Some(command);
    }
    None
}

#[derive(Clone, Debug, PartialEq)]
pub enum Sandbox {
    Ready,
    Missing,
    /// Installed but the system does not let it make a sandbox.
    Blocked(String),
}

/// Whether bubblewrap can make the same kind of sandbox the game runs in.
pub fn sandbox() -> Sandbox {
    let Some(bwrap) = which("bwrap") else { return Sandbox::Missing };
    let child = command(bwrap)
        .args(["--die-with-parent", "--unshare-pid", "--ro-bind", "/", "/", "--dev", "/dev", "--proc", "/proc", "--", "true"])
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::piped())
        .spawn();
    let Ok(mut child) = child else { return Sandbox::Blocked("it could not be started".into()) };
    let deadline = Instant::now() + Duration::from_secs(8);
    loop {
        match child.try_wait() {
            Ok(Some(status)) if status.success() => return Sandbox::Ready,
            Ok(Some(_)) => {
                let mut error = String::new();
                if let Some(mut stderr) = child.stderr.take() {
                    let _ = std::io::Read::read_to_string(&mut stderr, &mut error);
                }
                let error = error.lines().next().unwrap_or("it stopped with an error").trim_start_matches("bwrap: ").to_string();
                return Sandbox::Blocked(error);
            }
            Ok(None) if Instant::now() < deadline => std::thread::sleep(Duration::from_millis(20)),
            _ => {
                let _ = child.kill();
                return Sandbox::Blocked("it did not answer".into());
            }
        }
    }
}

/// Free memory, as the kernel estimates it for new programs.
pub fn memory_available() -> Option<u64> {
    let text = std::fs::read_to_string("/proc/meminfo").ok()?;
    let line = text.lines().find(|l| l.starts_with("MemAvailable:"))?;
    let kib: u64 = line.split_whitespace().nth(1)?.parse().ok()?;
    Some(kib * 1024)
}

pub fn size_of(path: &Path) -> u64 {
    let mut total = 0;
    let mut stack = vec![path.to_path_buf()];
    while let Some(dir) = stack.pop() {
        let Ok(entries) = std::fs::read_dir(&dir) else { continue };
        for entry in entries.flatten() {
            match entry.file_type() {
                Ok(kind) if kind.is_dir() => stack.push(entry.path()),
                Ok(kind) if kind.is_file() => total += entry.metadata().map(|m| m.len()).unwrap_or(0),
                _ => {}
            }
        }
    }
    total
}

pub fn human_size(bytes: u64) -> String {
    let gb = bytes as f64 / 1e9;
    if gb >= 1.0 { format!("{gb:.1} GB") } else { format!("{:.0} MB", bytes as f64 / 1e6) }
}

/// The system's monospace font, for logs.
pub fn monospace_family() -> String {
    Command::new("fc-match")
        .args(["-f", "%{family[0]}", "monospace"])
        .output()
        .ok()
        .filter(|out| out.status.success())
        .and_then(|out| String::from_utf8(out.stdout).ok())
        .filter(|family| !family.trim().is_empty())
        .unwrap_or_else(|| "DejaVu Sans Mono".into())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn install_commands_follow_the_distribution() {
        let ids = parse_os_release("NAME=\"Linux Mint\"\nID=linuxmint\nID_LIKE=\"ubuntu debian\"\n");
        assert_eq!(ids, ["linuxmint", "ubuntu", "debian"]);
        assert_eq!(install_command(&ids, "bubblewrap").as_deref(), Some("sudo apt install bubblewrap"));
        let fedora = parse_os_release("ID=fedora\nVERSION_ID=41\n");
        assert_eq!(install_command(&fedora, "bubblewrap").as_deref(), Some("sudo dnf install bubblewrap"));
        let derived = parse_os_release("ID=myos\nID_LIKE=arch\n");
        assert_eq!(install_command(&derived, "bubblewrap").as_deref(), Some("sudo pacman -S bubblewrap"));
        assert_eq!(install_command(&parse_os_release("ID=unknownos\n"), "bubblewrap"), None);
    }

    #[test]
    fn sizes_read_well() {
        assert_eq!(human_size(1_400_000_000), "1.4 GB");
        assert_eq!(human_size(214_000_000), "214 MB");
    }
}
