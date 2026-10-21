//! Where the launcher keeps its files, and where its bundled mod files are.

use serde::Deserialize;
use std::path::{Path, PathBuf};

pub fn home() -> PathBuf {
    std::env::var_os("HOME").map(PathBuf::from).unwrap_or_else(|| PathBuf::from("/"))
}

fn xdg(variable: &str, fallback: &str) -> PathBuf {
    match std::env::var_os(variable) {
        Some(value) if Path::new(&value).is_absolute() => PathBuf::from(value),
        _ => home().join(fallback),
    }
}

/// The data root shared with the mod's scripts: `KIRBY_DATA_ROOT` when set
/// (#219), otherwise `~/.local/share/kirbys-dead-land`.
pub fn data_root() -> PathBuf {
    match std::env::var_os("KIRBY_DATA_ROOT") {
        Some(value) if !value.is_empty() => absolute(&expand(Path::new(&value))),
        _ => xdg("XDG_DATA_HOME", ".local/share").join("kirbys-dead-land"),
    }
}

pub fn config_dir() -> PathBuf {
    xdg("XDG_CONFIG_HOME", ".config").join("kirbys-dead-land")
}

pub fn expand(path: &Path) -> PathBuf {
    match path.strip_prefix("~") {
        Ok(rest) => home().join(rest),
        Err(_) => path.to_path_buf(),
    }
}

fn absolute(path: &Path) -> PathBuf {
    if path.is_absolute() {
        path.to_path_buf()
    } else {
        std::env::current_dir().map(|dir| dir.join(path)).unwrap_or_else(|_| path.to_path_buf())
    }
}

/// A path as the player would write it, with `~` for their home folder.
pub fn pretty(path: &Path) -> String {
    match path.strip_prefix(home()) {
        Ok(rest) if rest.as_os_str().is_empty() => "~".into(),
        Ok(rest) => format!("~/{}", rest.display()),
        Err(_) => path.display().to_string(),
    }
}

/// The files the launcher ships with: the mod's scripts and tools with its
/// built runtime (`project/`), and the bridge and oracle runner (`bin/`).
#[derive(Clone, Debug)]
pub struct Bundle {
    pub root: PathBuf,
    pub version: String,
    pub commit: String,
}

#[derive(Deserialize)]
struct Manifest {
    version: String,
    #[serde(default)]
    commit: String,
}

impl Bundle {
    pub fn project(&self) -> PathBuf {
        self.root.join("project")
    }

    pub fn bridge(&self) -> PathBuf {
        self.root.join("bin/libkssu_bridge.so")
    }

    pub fn runner(&self) -> PathBuf {
        self.root.join("bin/kssu-oracle-runner")
    }

    /// What a finished setup records, so a new bundle sets the mod up again.
    pub fn id(&self) -> String {
        if self.commit.is_empty() { self.version.clone() } else { format!("{}+{}", self.version, self.commit) }
    }

    pub fn open(root: &Path) -> Option<Bundle> {
        let manifest: Manifest = serde_json::from_slice(&std::fs::read(root.join("bundle.json")).ok()?).ok()?;
        let bundle = Bundle { root: root.to_path_buf(), version: manifest.version, commit: manifest.commit };
        let complete = bundle.project().join("scripts/launch_runtime.py").is_file()
            && bundle.project().join("src/runtime/bin/Release/net10.0/output/KirbySuperStarUltra/Kirby.Runtime.dll").is_file()
            && bundle.bridge().is_file()
            && bundle.runner().is_file();
        complete.then_some(bundle)
    }
}

/// The bundle beside this launcher: `--bundle`, `KIRBYS_DEAD_LAND_BUNDLE`, the
/// AppImage's own files, or `share/kirbys-dead-land` next to the program.
pub fn find_bundle(explicit: Option<PathBuf>) -> Option<Bundle> {
    let mut candidates = Vec::new();
    candidates.extend(explicit);
    candidates.extend(std::env::var_os("KIRBYS_DEAD_LAND_BUNDLE").map(PathBuf::from));
    candidates.extend(std::env::var_os("APPDIR").map(|dir| PathBuf::from(dir).join("usr/share/kirbys-dead-land")));
    if let Some(dir) = std::env::current_exe().ok().and_then(|exe| exe.parent().map(Path::to_path_buf)) {
        candidates.push(dir.join("../share/kirbys-dead-land"));
        candidates.push(dir.join("share/kirbys-dead-land"));
    }
    candidates.iter().find_map(|root| Bundle::open(&absolute(&expand(root))))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn pretty_paths_use_the_home_folder() {
        let home = home();
        assert_eq!(pretty(&home.join("Documents/rom.nds")), "~/Documents/rom.nds");
        assert_eq!(pretty(&home), "~");
        assert_eq!(pretty(Path::new("/opt/x")), "/opt/x");
    }

    #[test]
    fn bundle_needs_every_part() {
        let root = std::env::temp_dir().join(format!("kdl-bundle-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(root.join("project/scripts")).unwrap();
        std::fs::write(root.join("bundle.json"), r#"{"version": "0.2.0", "commit": "abc123"}"#).unwrap();
        std::fs::write(root.join("project/scripts/launch_runtime.py"), "").unwrap();
        assert!(Bundle::open(&root).is_none());
        let runtime = root.join("project/src/runtime/bin/Release/net10.0/output/KirbySuperStarUltra");
        std::fs::create_dir_all(&runtime).unwrap();
        std::fs::write(runtime.join("Kirby.Runtime.dll"), "").unwrap();
        std::fs::create_dir_all(root.join("bin")).unwrap();
        std::fs::write(root.join("bin/libkssu_bridge.so"), "").unwrap();
        assert!(Bundle::open(&root).is_none());
        std::fs::write(root.join("bin/kssu-oracle-runner"), "").unwrap();
        let bundle = Bundle::open(&root).unwrap();
        assert_eq!(bundle.id(), "0.2.0+abc123");
        std::fs::remove_dir_all(&root).unwrap();
    }
}
