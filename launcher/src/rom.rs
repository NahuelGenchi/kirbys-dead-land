//! Checks that a file is the player's Kirby Super Star Ultra USA revision 0 ROM,
//! and says plainly what it is when it is not. The ROM is only read.

use sha2::{Digest, Sha256};
use std::io::Read;
use std::path::{Path, PathBuf};

pub const SHA256: &str = "b2573313a582c8dfaf2d93834a670c1ca8f389e5daa744e2dbe1a4ca903f3027";
pub const SIZE: u64 = 128 * 1024 * 1024;
const HEADER: usize = 0x200;

#[derive(Clone, Debug, PartialEq)]
pub enum Verdict {
    Verified,
    Missing,
    Unreadable(String),
    Archive,
    NotNds,
    OtherGame(String),
    Region(&'static str),
    Revision(u8),
    Trimmed,
    Modified,
}

impl Verdict {
    pub fn ok(&self) -> bool {
        *self == Verdict::Verified
    }

    pub fn message(&self) -> String {
        match self {
            Verdict::Verified => "USA · revision 0 · verified".into(),
            Verdict::Missing => "The file is no longer there. Choose it again.".into(),
            Verdict::Unreadable(why) => format!("This file can't be read ({why})."),
            Verdict::Archive => "This is a compressed archive. Extract the .nds file from it first, then choose that file.".into(),
            Verdict::NotNds => "This isn't a Nintendo DS game file. Choose the .nds file of Kirby Super Star Ultra.".into(),
            Verdict::OtherGame(title) => format!("This is a different DS game (“{title}”). Choose Kirby Super Star Ultra."),
            Verdict::Region(region) => format!("This is the {region} version. The mod needs the USA version (revision 0)."),
            Verdict::Revision(n) => format!("This is revision {n} of the USA version. The mod needs revision 0, the first release."),
            Verdict::Trimmed => "This copy was trimmed to save space. Use an untrimmed copy of the USA version.".into(),
            Verdict::Modified => "This copy differs from the original USA revision 0; it may be patched or damaged. Use an unmodified copy.".into(),
        }
    }
}

fn region(code: u8) -> &'static str {
    match code {
        b'J' => "Japanese",
        b'P' => "European",
        b'K' => "Korean",
        b'U' => "Australian",
        b'C' => "Chinese",
        _ => "another region's",
    }
}

/// What a file is, from its first bytes, its length and (for a USA revision 0
/// header) its SHA-256.
pub fn classify(header: &[u8], len: u64, digest: Option<&str>) -> Verdict {
    let magic = |m: &[u8]| header.starts_with(m);
    if magic(b"PK\x03\x04") || magic(b"7z\xbc\xaf\x27\x1c") || magic(b"Rar!") || magic(b"\x1f\x8b") || magic(b"\xfd7zXZ") {
        return Verdict::Archive;
    }
    if header.len() < HEADER {
        return Verdict::NotNds;
    }
    let code = &header[0x0c..0x10];
    let printable = |bytes: &[u8]| bytes.iter().all(|b| b.is_ascii_alphanumeric());
    // A DS header has an uppercase game code and a checksum of its first 0x15E bytes.
    if !printable(code) || crc16(&header[..0x15e]) != u16::from_le_bytes([header[0x15e], header[0x15f]]) {
        return Verdict::NotNds;
    }
    if &code[..3] != b"YKW" {
        let title = String::from_utf8_lossy(&header[..0x0c]).trim_matches(|c: char| c == '\0' || c.is_whitespace()).to_string();
        return Verdict::OtherGame(if title.is_empty() { String::from_utf8_lossy(code).into() } else { title });
    }
    if code[3] != b'E' {
        return Verdict::Region(region(code[3]));
    }
    if header[0x1e] != 0 {
        return Verdict::Revision(header[0x1e]);
    }
    match digest {
        Some(SHA256) => Verdict::Verified,
        _ if len < SIZE => Verdict::Trimmed,
        _ => Verdict::Modified,
    }
}

/// The CRC-16 (MODBUS) the DS header stores at 0x15E.
fn crc16(data: &[u8]) -> u16 {
    let mut crc: u16 = 0xffff;
    for &byte in data {
        crc ^= byte as u16;
        for _ in 0..8 {
            crc = if crc & 1 != 0 { (crc >> 1) ^ 0xa001 } else { crc >> 1 };
        }
    }
    crc
}

pub fn sha256(path: &Path) -> std::io::Result<String> {
    let mut file = std::fs::File::open(path)?;
    let mut hasher = Sha256::new();
    let mut buffer = vec![0; 1 << 20];
    loop {
        let n = file.read(&mut buffer)?;
        if n == 0 {
            break;
        }
        hasher.update(&buffer[..n]);
    }
    Ok(hasher.finalize().iter().map(|b| format!("{b:02x}")).collect())
}

pub fn verify(path: &Path) -> Verdict {
    let metadata = match std::fs::metadata(path) {
        Ok(metadata) if metadata.is_file() => metadata,
        Ok(_) => return Verdict::NotNds,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Verdict::Missing,
        Err(error) => return Verdict::Unreadable(error.kind().to_string()),
    };
    let mut header = Vec::with_capacity(HEADER);
    if let Err(error) = std::fs::File::open(path).and_then(|file| file.take(HEADER as u64).read_to_end(&mut header)) {
        return Verdict::Unreadable(error.kind().to_string());
    }
    match classify(&header, metadata.len(), None) {
        Verdict::Trimmed | Verdict::Modified => match sha256(path) {
            Ok(digest) => classify(&header, metadata.len(), Some(&digest)),
            Err(error) => Verdict::Unreadable(error.kind().to_string()),
        },
        other => other,
    }
}

/// A verified ROM in the usual places, for the player to confirm. Only `.nds`
/// files of the right size are hashed, and the search stops early.
pub fn find_candidate(home: &Path) -> Option<PathBuf> {
    let folders = ["Downloads", "Documents", "Desktop", "ROMs", "roms", "Roms", "Games", "games", "Emulation"];
    let mut budget = 25_000usize;
    for folder in folders {
        let mut stack = vec![(home.join(folder), 0)];
        while let Some((dir, depth)) = stack.pop() {
            let Ok(entries) = std::fs::read_dir(&dir) else { continue };
            for entry in entries.flatten() {
                budget = budget.checked_sub(1)?;
                let path = entry.path();
                let name = entry.file_name();
                let Ok(kind) = entry.file_type() else { continue };
                if kind.is_dir() && depth < 4 && !name.to_string_lossy().starts_with('.') {
                    stack.push((path, depth + 1));
                } else if kind.is_file()
                    && path.extension().is_some_and(|e| e.eq_ignore_ascii_case("nds"))
                    && entry.metadata().is_ok_and(|m| m.len() == SIZE)
                    && verify(&path).ok()
                {
                    return Some(path);
                }
            }
        }
    }
    None
}

#[cfg(test)]
mod tests {
    use super::*;

    fn header(title: &[u8], code: &[u8; 4], revision: u8) -> Vec<u8> {
        let mut h = vec![0u8; HEADER];
        h[..title.len()].copy_from_slice(title);
        h[0x0c..0x10].copy_from_slice(code);
        h[0x10..0x12].copy_from_slice(b"01");
        h[0x1e] = revision;
        let crc = crc16(&h[..0x15e]);
        h[0x15e..0x160].copy_from_slice(&crc.to_le_bytes());
        h
    }

    #[test]
    fn the_usa_first_release_is_verified_by_hash() {
        let h = header(b"KIRBY USDX E", b"YKWE", 0);
        assert_eq!(classify(&h, SIZE, Some(SHA256)), Verdict::Verified);
        assert_eq!(classify(&h, SIZE, Some("00")), Verdict::Modified);
        assert_eq!(classify(&h, 76 * 1024 * 1024, Some("00")), Verdict::Trimmed);
        assert!(Verdict::Verified.ok());
    }

    #[test]
    fn other_regions_and_revisions_are_named() {
        assert_eq!(classify(&header(b"KIRBY USDX P", b"YKWP", 0), SIZE, None), Verdict::Region("European"));
        assert_eq!(classify(&header(b"KIRBY USDX J", b"YKWJ", 0), SIZE, None), Verdict::Region("Japanese"));
        assert_eq!(classify(&header(b"KIRBY USDX K", b"YKWK", 0), SIZE, None), Verdict::Region("Korean"));
        assert_eq!(classify(&header(b"KIRBY USDX E", b"YKWE", 1), SIZE, None), Verdict::Revision(1));
        assert!(Verdict::Region("European").message().contains("European version"));
        assert!(Verdict::Revision(1).message().contains("revision 1"));
    }

    #[test]
    fn other_files_are_explained() {
        assert_eq!(classify(&header(b"MARIO KART", b"AMCE", 0), SIZE, None), Verdict::OtherGame("MARIO KART".into()));
        assert_eq!(classify(b"PK\x03\x04rest of a zip", 100, None), Verdict::Archive);
        assert_eq!(classify(b"7z\xbc\xaf\x27\x1c....", 100, None), Verdict::Archive);
        assert_eq!(classify(b"short", 5, None), Verdict::NotNds);
        let mut broken = header(b"KIRBY USDX E", b"YKWE", 0);
        broken[0x15e] ^= 0xff;
        assert_eq!(classify(&broken, SIZE, None), Verdict::NotNds);
        assert_eq!(classify(&vec![0xffu8; HEADER], SIZE, None), Verdict::NotNds);
    }

    #[test]
    fn missing_files_and_folders() {
        assert_eq!(verify(Path::new("/nonexistent/kssu.nds")), Verdict::Missing);
        assert_eq!(verify(&std::env::temp_dir()), Verdict::NotNds);
    }

    #[test]
    fn header_checksum_matches_the_known_algorithm() {
        // CRC-16/MODBUS check value.
        assert_eq!(crc16(b"123456789"), 0x4b37);
    }
}
