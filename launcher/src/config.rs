//! The player's choices (`~/.config/kirbys-dead-land/config.json`) and what the
//! last finished setup was built from (`<data root>/setup.json`).

use serde::{de::DeserializeOwned, Deserialize, Serialize};
use std::path::{Path, PathBuf};

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(default)]
pub struct Config {
    /// The ROM the player chose. It is checked again on every start.
    pub rom: Option<PathBuf>,
    /// Look for a new launcher version when it opens.
    pub auto_update: bool,
}

impl Default for Config {
    fn default() -> Self {
        Config { rom: None, auto_update: true }
    }
}

/// What a finished setup used. Any difference sets the changed parts up again.
#[derive(Clone, Debug, Default, PartialEq, Serialize, Deserialize)]
#[serde(default)]
pub struct SetupRecord {
    pub bundle: String,
    pub game: Option<PathBuf>,
    pub rom: Option<PathBuf>,
    pub outfit_bundle: String,
    pub outfit_game: Option<PathBuf>,
    pub finished: bool,
}

fn load<T: DeserializeOwned + Default>(path: &Path) -> T {
    std::fs::read(path).ok().and_then(|bytes| serde_json::from_slice(&bytes).ok()).unwrap_or_default()
}

fn save<T: Serialize>(value: &T, path: &Path) -> std::io::Result<()> {
    if let Some(dir) = path.parent() {
        std::fs::create_dir_all(dir)?;
    }
    let partial = path.with_extension("json.partial");
    std::fs::write(&partial, serde_json::to_vec_pretty(value).map_err(std::io::Error::other)?)?;
    std::fs::rename(&partial, path)
}

impl Config {
    pub fn path(dir: &Path) -> PathBuf {
        dir.join("config.json")
    }
    pub fn load(dir: &Path) -> Config {
        load(&Config::path(dir))
    }
    pub fn save(&self, dir: &Path) -> std::io::Result<()> {
        save(self, &Config::path(dir))
    }
}

impl SetupRecord {
    pub fn path(root: &Path) -> PathBuf {
        root.join("setup.json")
    }
    pub fn load(root: &Path) -> SetupRecord {
        load(&SetupRecord::path(root))
    }
    pub fn save(&self, root: &Path) -> std::io::Result<()> {
        save(self, &SetupRecord::path(root))
    }

    /// Whether play can start without setting anything up again.
    pub fn ready_for(&self, bundle: &str, game: &Path, rom: &Path) -> bool {
        self.finished && self.bundle == bundle && self.game.as_deref() == Some(game) && self.rom.as_deref() == Some(rom)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn scratch(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("kdl-config-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        dir
    }

    #[test]
    fn config_round_trips_and_defaults() {
        let dir = scratch("trip");
        assert_eq!(Config::load(&dir), Config::default());
        assert!(Config::default().auto_update);
        let config = Config { rom: Some("/games/kssu.nds".into()), auto_update: false };
        config.save(&dir).unwrap();
        assert_eq!(Config::load(&dir), config);
        std::fs::remove_dir_all(&dir).unwrap();
    }

    #[test]
    fn damaged_or_partial_files_fall_back() {
        let dir = scratch("damaged");
        std::fs::create_dir_all(&dir).unwrap();
        std::fs::write(Config::path(&dir), b"{not json").unwrap();
        assert_eq!(Config::load(&dir), Config::default());
        std::fs::write(Config::path(&dir), br#"{"rom": "/a.nds", "future": 1}"#).unwrap();
        assert_eq!(Config::load(&dir), Config { rom: Some("/a.nds".into()), auto_update: true });
        std::fs::remove_dir_all(&dir).unwrap();
    }

    #[test]
    fn setup_is_ready_only_for_the_same_inputs() {
        let record = SetupRecord {
            bundle: "0.2.0+abc".into(),
            game: Some("/steam/Dead Cells".into()),
            rom: Some("/roms/kssu.nds".into()),
            finished: true,
            ..Default::default()
        };
        assert!(record.ready_for("0.2.0+abc", Path::new("/steam/Dead Cells"), Path::new("/roms/kssu.nds")));
        assert!(!record.ready_for("0.2.1+def", Path::new("/steam/Dead Cells"), Path::new("/roms/kssu.nds")));
        assert!(!record.ready_for("0.2.0+abc", Path::new("/other/Dead Cells"), Path::new("/roms/kssu.nds")));
        assert!(!record.ready_for("0.2.0+abc", Path::new("/steam/Dead Cells"), Path::new("/moved/kssu.nds")));
        assert!(!SetupRecord { finished: false, ..record }.ready_for("0.2.0+abc", Path::new("/steam/Dead Cells"), Path::new("/roms/kssu.nds")));
    }
}
