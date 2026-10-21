//! Asks GitHub for the newest regular release. Only the version is compared;
//! nothing is downloaded or installed by the launcher itself.

use std::cmp::Ordering;
use std::time::Duration;

pub const REPOSITORY: &str = "https://github.com/NahuelGenchi/kirbys-dead-land";
const LATEST: &str = "https://api.github.com/repos/NahuelGenchi/kirbys-dead-land/releases/latest";

pub struct Release {
    pub version: String,
    pub url: String,
}

/// Compares dotted version numbers, ignoring a leading `v` and any suffix.
pub fn compare(a: &str, b: &str) -> Ordering {
    let parts = |v: &str| -> Vec<u64> {
        v.trim_start_matches('v')
            .split(['-', '+'])
            .next()
            .unwrap_or("")
            .split('.')
            .map(|p| p.parse().unwrap_or(0))
            .collect()
    };
    let (a, b) = (parts(a), parts(b));
    for i in 0..a.len().max(b.len()) {
        match a.get(i).unwrap_or(&0).cmp(b.get(i).unwrap_or(&0)) {
            Ordering::Equal => continue,
            other => return other,
        }
    }
    Ordering::Equal
}

/// The latest release when it is newer than `current`.
pub fn newer_than(current: &str) -> Result<Option<Release>, String> {
    let agent = ureq::Agent::config_builder()
        .timeout_global(Some(Duration::from_secs(20)))
        .user_agent(concat!("kirbys-dead-land-launcher/", env!("CARGO_PKG_VERSION")))
        .build()
        .new_agent();
    let mut response = agent
        .get(LATEST)
        .header("Accept", "application/vnd.github+json")
        .call()
        .map_err(|e| e.to_string())?;
    let text = response.body_mut().read_to_string().map_err(|e| e.to_string())?;
    let body: serde_json::Value = serde_json::from_str(&text).map_err(|e| e.to_string())?;
    let tag = body["tag_name"].as_str().ok_or("no release")?.to_string();
    let url = body["html_url"].as_str().unwrap_or(REPOSITORY).to_string();
    Ok((compare(&tag, current) == Ordering::Greater).then(|| Release { version: tag.trim_start_matches('v').to_string(), url }))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn versions_compare_by_number() {
        assert_eq!(compare("v0.10.0", "0.9.3"), Ordering::Greater);
        assert_eq!(compare("0.2.0", "v0.2.0"), Ordering::Equal);
        assert_eq!(compare("0.2", "0.2.0"), Ordering::Equal);
        assert_eq!(compare("0.2.0-rc1", "0.2.0"), Ordering::Equal);
        assert_eq!(compare("1.0.0", "1.0.1"), Ordering::Less);
    }
}
