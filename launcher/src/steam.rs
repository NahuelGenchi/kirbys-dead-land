//! Finds Steam (native, Flatpak or Snap on Linux; the registry's on Windows),
//! every Steam library, and Dead Cells in them, by reading Steam's own files.
//! Nothing in Steam is changed.

use std::path::{Path, PathBuf};

pub const APP_ID: &str = "588650";
pub const TESTED_BUILD: &str = "23762174";

#[derive(Clone, Copy, Debug, PartialEq)]
pub enum Kind {
    Native,
    // Linux only.
    #[cfg_attr(windows, allow(dead_code))]
    Flatpak,
    #[cfg_attr(windows, allow(dead_code))]
    Snap,
}

impl Kind {
    pub fn label(self) -> &'static str {
        match self {
            Kind::Native => "Steam",
            Kind::Flatpak => "Steam (Flatpak)",
            Kind::Snap => "Steam (Snap)",
        }
    }
}

#[derive(Clone, Debug, PartialEq)]
pub struct Steam {
    pub root: PathBuf,
    pub kind: Kind,
}

#[derive(Clone, Debug, PartialEq)]
pub struct Game {
    pub dir: PathBuf,
    pub build: Option<String>,
    pub steam: Steam,
}

#[derive(Clone, Debug, PartialEq)]
pub enum GameStatus {
    Ready(Game),
    /// Installed through Proton: the Windows files instead of the Linux ones.
    Windows(Game),
    /// Listed by Steam, but files are missing (downloading or damaged).
    Incomplete(Game),
    Missing,
}

/// Steam's text "KeyValues" format, as in `libraryfolders.vdf` and `.acf` files.
pub mod vdf {
    #[derive(Clone, Debug, PartialEq)]
    pub enum Value {
        Text(String),
        Map(Vec<(String, Value)>),
    }

    impl Value {
        /// A child by key, ignoring case as Steam does.
        pub fn get(&self, key: &str) -> Option<&Value> {
            match self {
                Value::Map(entries) => entries.iter().find(|(k, _)| k.eq_ignore_ascii_case(key)).map(|(_, v)| v),
                Value::Text(_) => None,
            }
        }

        pub fn text(&self) -> Option<&str> {
            match self {
                Value::Text(text) => Some(text),
                Value::Map(_) => None,
            }
        }

        pub fn entries(&self) -> &[(String, Value)] {
            match self {
                Value::Map(entries) => entries,
                Value::Text(_) => &[],
            }
        }
    }

    enum Token {
        Text(String),
        Open,
        Close,
    }

    fn tokens(source: &str) -> Option<Vec<Token>> {
        let mut out = Vec::new();
        let mut chars = source.chars().peekable();
        while let Some(c) = chars.next() {
            match c {
                '{' => out.push(Token::Open),
                '}' => out.push(Token::Close),
                '"' => {
                    let mut text = String::new();
                    loop {
                        match chars.next()? {
                            '"' => break,
                            '\\' => match chars.next()? {
                                'n' => text.push('\n'),
                                't' => text.push('\t'),
                                other => text.push(other),
                            },
                            other => text.push(other),
                        }
                    }
                    out.push(Token::Text(text));
                }
                '/' if chars.peek() == Some(&'/') => {
                    for c in chars.by_ref() {
                        if c == '\n' {
                            break;
                        }
                    }
                }
                c if c.is_whitespace() => {}
                c => {
                    // An unquoted word, such as a conditional like [$WIN32].
                    let mut text = c.to_string();
                    while let Some(&next) = chars.peek() {
                        if next.is_whitespace() || next == '{' || next == '}' || next == '"' {
                            break;
                        }
                        text.push(next);
                        chars.next();
                    }
                    out.push(Token::Text(text));
                }
            }
        }
        Some(out)
    }

    fn map(tokens: &mut std::iter::Peekable<std::vec::IntoIter<Token>>, nested: bool) -> Option<Value> {
        let mut entries = Vec::new();
        loop {
            match tokens.next() {
                None if !nested => return Some(Value::Map(entries)),
                None => return None,
                Some(Token::Close) if nested => return Some(Value::Map(entries)),
                Some(Token::Close) | Some(Token::Open) => return None,
                Some(Token::Text(key)) => {
                    let value = match tokens.next()? {
                        Token::Text(text) => Value::Text(text),
                        Token::Open => map(tokens, true)?,
                        Token::Close => return None,
                    };
                    // Skip a conditional after a value, as in `"key" "value" [$LINUX]`.
                    if let Some(Token::Text(next)) = tokens.peek() {
                        if next.starts_with('[') {
                            tokens.next();
                        }
                    }
                    entries.push((key, value));
                }
            }
        }
    }

    pub fn parse(source: &str) -> Option<Value> {
        let mut tokens = tokens(source)?.into_iter().peekable();
        map(&mut tokens, false)
    }
}

/// A folder's real path, to tell two names for one folder apart. Windows paths
/// keep their usual form, without the `\\?\` that canonicalizing adds there.
fn real(path: &Path) -> PathBuf {
    let Ok(real) = path.canonicalize() else { return path.to_path_buf() };
    #[cfg(windows)]
    if let Some(rest) = real.to_str().and_then(|s| s.strip_prefix(r"\\?\")).filter(|rest| rest.as_bytes().get(1) == Some(&b':')) {
        return PathBuf::from(rest);
    }
    real
}

/// The Steam installation the Steam client records in the registry, then the
/// installer's default folder, each once (#250).
#[cfg(windows)]
pub fn installations(_home: &Path) -> Vec<Steam> {
    // Steam records it with forward slashes, often in lower case.
    let recorded = crate::windows::user_registry_text(r"Software\Valve\Steam", "SteamPath").map(|path| PathBuf::from(path.replace('/', "\\")));
    let default = std::env::var_os("ProgramFiles(x86)").map(|dir| PathBuf::from(dir).join("Steam"));
    let mut found: Vec<Steam> = Vec::new();
    for root in recorded.into_iter().chain(default) {
        let root = real(&root);
        if root.join("steamapps").is_dir() && !found.iter().any(|steam| steam.root == root) {
            found.push(Steam { root, kind: Kind::Native });
        }
    }
    found
}

/// Every Steam installation in the player's home folder, each once.
#[cfg(unix)]
pub fn installations(home: &Path) -> Vec<Steam> {
    let candidates = [
        (".local/share/Steam", Kind::Native),
        (".steam/steam", Kind::Native),
        (".steam/root", Kind::Native),
        (".steam/debian-installation", Kind::Native),
        (".var/app/com.valvesoftware.Steam/.local/share/Steam", Kind::Flatpak),
        (".var/app/com.valvesoftware.Steam/data/Steam", Kind::Flatpak),
        ("snap/steam/common/.local/share/Steam", Kind::Snap),
    ];
    let mut found: Vec<Steam> = Vec::new();
    let mut seen = Vec::new();
    for (relative, kind) in candidates {
        let root = home.join(relative);
        if !root.join("steamapps").is_dir() {
            continue;
        }
        let real = real(&root);
        if !seen.contains(&real) {
            seen.push(real.clone());
            found.push(Steam { root: real, kind });
        }
    }
    found
}

/// Every library folder of one Steam installation, its own first.
pub fn libraries(steam: &Path) -> Vec<PathBuf> {
    let mut libraries = vec![steam.to_path_buf()];
    let file = steam.join("steamapps").join("libraryfolders.vdf");
    if let Some(root) = std::fs::read_to_string(file).ok().and_then(|text| vdf::parse(&text)) {
        let folders = root.get("libraryfolders").or_else(|| root.get("LibraryFolders"));
        for (_, entry) in folders.map(vdf::Value::entries).unwrap_or_default() {
            // Newer files nest a map with "path"; older ones give the path directly.
            let path = entry.get("path").and_then(vdf::Value::text).or_else(|| entry.text());
            if let Some(path) = path.filter(|p| Path::new(p).is_absolute()) {
                let real = real(Path::new(path));
                if !libraries.contains(&real) {
                    libraries.push(real);
                }
            }
        }
    }
    libraries
}

/// Reads `appmanifest_588650.acf`: the install folder name and the build ID.
pub fn read_manifest(text: &str) -> Option<(String, Option<String>)> {
    let root = vdf::parse(text)?;
    let state = root.get("AppState")?;
    let dir = state.get("installdir")?.text()?.to_string();
    let build = state.get("buildid").and_then(vdf::Value::text).map(str::to_string);
    (!dir.is_empty() && !dir.contains(['/', '\\'])).then_some((dir, build))
}

#[derive(Debug, PartialEq)]
enum Files {
    Ready,
    Windows,
    Incomplete,
}

/// What the game folder's files make it, given which exist. On Windows the
/// Windows game is the one to play: its code is in `deadcells_gl.exe`, the files
/// `scripts/stage_runtime.py` places there (#246).
fn files(has: impl Fn(&str) -> bool, windows_host: bool) -> Files {
    let needed: &[&str] = if windows_host { &["deadcells_gl.exe", "res.pak"] } else { &["hlboot.dat", "res.pak", "steam.hdll", "libsteam_api.so"] };
    if !windows_host && !has("deadcells") && (has("deadcells.exe") || has("DeadCells.exe")) {
        Files::Windows
    } else if needed.iter().all(|name| has(name)) {
        Files::Ready
    } else {
        Files::Incomplete
    }
}

fn status(game: Game) -> GameStatus {
    match files(|name| game.dir.join(name).is_file(), cfg!(windows)) {
        Files::Ready => GameStatus::Ready(game),
        Files::Windows => GameStatus::Windows(game),
        Files::Incomplete => GameStatus::Incomplete(game),
    }
}

/// Dead Cells in any library of any Steam installation; a ready Linux install
/// wins over a Windows or incomplete one.
pub fn find_game(steams: &[Steam]) -> GameStatus {
    let mut fallback = GameStatus::Missing;
    for steam in steams {
        for library in libraries(&steam.root) {
            let manifest = library.join("steamapps").join(format!("appmanifest_{APP_ID}.acf"));
            let Some((dir, build)) = std::fs::read_to_string(&manifest).ok().and_then(|t| read_manifest(&t)) else {
                continue;
            };
            // One part at a time, so Windows paths keep their backslashes.
            let dir = library.join("steamapps").join("common").join(dir);
            if !dir.is_dir() {
                continue;
            }
            match status(Game { dir, build, steam: steam.clone() }) {
                ready @ GameStatus::Ready(_) => return ready,
                other if fallback == GameStatus::Missing => fallback = other,
                _ => {}
            }
        }
    }
    fallback
}

/// Whether a Steam client is running.
#[cfg(windows)]
pub fn running() -> bool {
    crate::windows::process_running("steam.exe")
}

/// Whether a Steam client is running for this user.
#[cfg(unix)]
pub fn running() -> bool {
    let uid = unsafe { libc::getuid() };
    let Ok(entries) = std::fs::read_dir("/proc") else { return false };
    entries.flatten().any(|entry| {
        let path = entry.path();
        let ours = std::fs::metadata(&path).map(|m| std::os::unix::fs::MetadataExt::uid(&m) == uid).unwrap_or(false);
        ours && std::fs::read_to_string(path.join("comm")).map(|name| name.trim() == "steam").unwrap_or(false)
    })
}

/// Starts Steam in the background, the way this installation is started.
#[cfg(windows)]
pub fn start(steam: &Steam) -> std::io::Result<()> {
    let program = steam.root.join("steam.exe");
    if program.is_file() { crate::system::spawn_detached(program, &["-silent"]) } else { crate::system::open("steam://open/main") }
}

/// Starts Steam in the background, the way this installation is started.
#[cfg(unix)]
pub fn start(steam: &Steam) -> std::io::Result<()> {
    let (program, args): (&str, &[&str]) = match steam.kind {
        Kind::Flatpak => ("flatpak", &["run", "com.valvesoftware.Steam", "-silent"]),
        Kind::Snap => ("snap", &["run", "steam", "-silent"]),
        Kind::Native if crate::system::which("steam").is_some() => ("steam", &["-silent"]),
        Kind::Native => ("xdg-open", &["steam://open/main"]),
    };
    crate::system::spawn_detached(program, args)
}

#[cfg(test)]
mod tests {
    use super::*;

    const LIBRARIES: &str = r#"
"libraryfolders"
{
	"0"
	{
		"path"		"/home/player/.local/share/Steam"
		"label"		""
		"apps"
		{
			"588650"		"2107319867"
		}
	}
	"1"
	{
		"path"		"/mnt/games/SteamLibrary"
		"label"		"Games \"SSD\""
	}
}
"#;

    const MANIFEST: &str = r#"
"AppState"
{
	"appid"		"588650"
	"name"		"Dead Cells"
	"StateFlags"		"4"
	"installdir"		"Dead Cells"
	"buildid"		"23762174"
	"UserConfig"
	{
		"language"		"english"
	}
}
"#;

    #[test]
    fn parses_library_folders() {
        let root = vdf::parse(LIBRARIES).unwrap();
        let folders = root.get("libraryfolders").unwrap();
        let paths: Vec<_> = folders.entries().iter().filter_map(|(_, v)| v.get("path")?.text()).collect();
        assert_eq!(paths, ["/home/player/.local/share/Steam", "/mnt/games/SteamLibrary"]);
        assert_eq!(folders.get("1").unwrap().get("label").unwrap().text(), Some("Games \"SSD\""));
    }

    #[test]
    fn parses_old_style_libraries_and_comments() {
        let root = vdf::parse("// written by Steam\n\"LibraryFolders\" { \"TimeNextStatsReport\" \"1\" \"1\" \"/media/old\" }").unwrap();
        let folders = root.get("libraryfolders").unwrap();
        assert_eq!(folders.get("1").unwrap().text(), Some("/media/old"));
    }

    #[test]
    fn rejects_broken_files() {
        assert_eq!(vdf::parse("\"a\" { \"b\" \"c\""), None);
        assert_eq!(vdf::parse("\"a\" }"), None);
        assert_eq!(vdf::parse("\"unterminated"), None);
    }

    #[test]
    fn reads_the_app_manifest() {
        assert_eq!(read_manifest(MANIFEST), Some(("Dead Cells".into(), Some("23762174".into()))));
        assert_eq!(read_manifest("\"AppState\" { \"installdir\" \"../escape\" }"), None);
        assert_eq!(read_manifest("\"AppState\" { \"appid\" \"1\" }"), None);
    }

    const GAME_FILES: &[&str] = if cfg!(windows) { &["deadcells.exe", "deadcells_gl.exe", "res.pak", "steam.hdll", "steam_api64.dll"] } else { &["deadcells", "hlboot.dat", "res.pak", "steam.hdll", "libsteam_api.so"] };

    fn fake_steam(home: &Path, files: &[&str]) -> Steam {
        let steam = home.join(".local/share/Steam");
        let common = steam.join("steamapps/common/Dead Cells");
        std::fs::create_dir_all(&common).unwrap();
        std::fs::write(steam.join(format!("steamapps/appmanifest_{APP_ID}.acf")), MANIFEST).unwrap();
        for file in files {
            std::fs::write(common.join(file), "").unwrap();
        }
        Steam { root: real(&steam), kind: Kind::Native }
    }

    #[test]
    #[cfg(unix)]
    fn finds_steam_in_the_home_folder() {
        let home = std::env::temp_dir().join(format!("kdl-steam-home-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&home);
        assert!(installations(&home).is_empty());
        let steam = fake_steam(&home, &[]);
        assert_eq!(installations(&home), [steam]);
        std::fs::remove_dir_all(&home).unwrap();
    }

    #[test]
    fn finds_the_game_and_its_build() {
        let home = std::env::temp_dir().join(format!("kdl-steam-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&home);
        let steam = fake_steam(&home, GAME_FILES);
        match find_game(&[steam]) {
            GameStatus::Ready(game) => {
                assert!(game.dir.ends_with("steamapps/common/Dead Cells"));
                assert_eq!(game.build.as_deref(), Some(TESTED_BUILD));
            }
            other => panic!("{other:?}"),
        }
        std::fs::remove_dir_all(&home).unwrap();
    }

    #[test]
    fn each_system_needs_its_own_game_files() {
        let has = |names: &'static [&'static str]| move |name: &str| names.contains(&name);
        let linux = &["deadcells", "hlboot.dat", "res.pak", "steam.hdll", "libsteam_api.so"];
        let windows = &["deadcells.exe", "deadcells_gl.exe", "res.pak", "steam.hdll", "steam_api64.dll"];
        assert_eq!(files(has(linux), false), Files::Ready);
        assert_eq!(files(has(windows), false), Files::Windows);
        assert_eq!(files(has(windows), true), Files::Ready);
        assert_eq!(files(has(&["deadcells_gl.exe", "res.pak"]), true), Files::Ready);
        assert_eq!(files(has(&["deadcells.exe", "res.pak"]), true), Files::Incomplete);
        assert_eq!(files(has(linux), true), Files::Incomplete);
    }

    #[test]
    fn incomplete_and_missing_games() {
        let home = std::env::temp_dir().join(format!("kdl-steam-partial-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&home);
        assert_eq!(find_game(&[]), GameStatus::Missing);
        let steam = fake_steam(&home, &["res.pak"]);
        assert!(matches!(find_game(&[steam]), GameStatus::Incomplete(_)));
        std::fs::remove_dir_all(&home).unwrap();
    }

    #[test]
    fn reads_other_libraries_in_this_systems_paths() {
        let home = std::env::temp_dir().join(format!("kdl-steam-libraries-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&home);
        let steam = fake_steam(&home, &[]);
        let other = home.join("Games Library");
        std::fs::create_dir_all(&other).unwrap();
        // Steam escapes the backslashes of Windows paths.
        let path = other.display().to_string().replace('\\', "\\\\");
        let text = format!("\"libraryfolders\" {{ \"0\" {{ \"path\" \"{}\" }} \"1\" {{ \"path\" \"{path}\" }} \"2\" {{ \"path\" \"relative\" }} }}", steam.root.display().to_string().replace('\\', "\\\\"));
        std::fs::write(steam.root.join("steamapps/libraryfolders.vdf"), text).unwrap();
        assert_eq!(libraries(&steam.root), [steam.root.clone(), real(&other)]);
        std::fs::remove_dir_all(&home).unwrap();
    }
}
