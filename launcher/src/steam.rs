//! Finds Steam (native, Flatpak or Snap), every Steam library, and Dead Cells
//! in them, by reading Steam's own files. Nothing in Steam is changed.

use std::path::{Path, PathBuf};

pub const APP_ID: &str = "588650";
pub const TESTED_BUILD: &str = "23762174";

#[derive(Clone, Copy, Debug, PartialEq)]
pub enum Kind {
    Native,
    Flatpak,
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

/// Every Steam installation in the player's home folder, each once.
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
        let real = root.canonicalize().unwrap_or_else(|_| root.clone());
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
    let file = steam.join("steamapps/libraryfolders.vdf");
    if let Some(root) = std::fs::read_to_string(file).ok().and_then(|text| vdf::parse(&text)) {
        let folders = root.get("libraryfolders").or_else(|| root.get("LibraryFolders"));
        for (_, entry) in folders.map(vdf::Value::entries).unwrap_or_default() {
            // Newer files nest a map with "path"; older ones give the path directly.
            let path = entry.get("path").and_then(vdf::Value::text).or_else(|| entry.text());
            if let Some(path) = path.filter(|p| p.starts_with('/')) {
                let path = PathBuf::from(path);
                let real = path.canonicalize().unwrap_or(path);
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
    (!dir.is_empty() && !dir.contains('/')).then_some((dir, build))
}

fn status(game: Game) -> GameStatus {
    let has = |name: &str| game.dir.join(name).is_file();
    if !has("deadcells") && (has("deadcells.exe") || has("DeadCells.exe")) {
        return GameStatus::Windows(game);
    }
    if ["hlboot.dat", "res.pak", "steam.hdll", "libsteam_api.so"].iter().all(|name| has(name)) {
        GameStatus::Ready(game)
    } else {
        GameStatus::Incomplete(game)
    }
}

/// Dead Cells in any library of any Steam installation; a ready Linux install
/// wins over a Windows or incomplete one.
pub fn find_game(steams: &[Steam]) -> GameStatus {
    let mut fallback = GameStatus::Missing;
    for steam in steams {
        for library in libraries(&steam.root) {
            let manifest = library.join(format!("steamapps/appmanifest_{APP_ID}.acf"));
            let Some((dir, build)) = std::fs::read_to_string(&manifest).ok().and_then(|t| read_manifest(&t)) else {
                continue;
            };
            let dir = library.join("steamapps/common").join(dir);
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

/// Whether a Steam client is running for this user.
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
pub fn start(kind: Kind) -> std::io::Result<()> {
    let (program, args): (&str, &[&str]) = match kind {
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

    fn fake_steam(home: &Path, files: &[&str]) -> PathBuf {
        let steam = home.join(".local/share/Steam");
        let common = steam.join("steamapps/common/Dead Cells");
        std::fs::create_dir_all(&common).unwrap();
        std::fs::write(steam.join(format!("steamapps/appmanifest_{APP_ID}.acf")), MANIFEST).unwrap();
        for file in files {
            std::fs::write(common.join(file), "").unwrap();
        }
        steam
    }

    #[test]
    fn finds_the_game_and_its_kind() {
        let home = std::env::temp_dir().join(format!("kdl-steam-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&home);
        fake_steam(&home, &["deadcells", "hlboot.dat", "res.pak", "steam.hdll", "libsteam_api.so"]);
        let steams = installations(&home);
        assert_eq!(steams.len(), 1);
        assert_eq!(steams[0].kind, Kind::Native);
        match find_game(&steams) {
            GameStatus::Ready(game) => {
                assert!(game.dir.ends_with("steamapps/common/Dead Cells"));
                assert_eq!(game.build.as_deref(), Some(TESTED_BUILD));
            }
            other => panic!("{other:?}"),
        }
        std::fs::remove_file(home.join(".local/share/Steam/steamapps/common/Dead Cells/deadcells")).unwrap();
        std::fs::write(home.join(".local/share/Steam/steamapps/common/Dead Cells/deadcells.exe"), "").unwrap();
        assert!(matches!(find_game(&steams), GameStatus::Windows(_)));
        std::fs::remove_dir_all(&home).unwrap();
    }

    #[test]
    fn incomplete_and_missing_games() {
        let home = std::env::temp_dir().join(format!("kdl-steam-partial-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&home);
        assert_eq!(find_game(&installations(&home)), GameStatus::Missing);
        fake_steam(&home, &["deadcells", "hlboot.dat"]);
        assert!(matches!(find_game(&installations(&home)), GameStatus::Incomplete(_)));
        std::fs::remove_dir_all(&home).unwrap();
    }
}
