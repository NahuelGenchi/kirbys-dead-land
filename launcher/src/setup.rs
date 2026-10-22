//! The one-time setup. It downloads the pinned helpers (hashes checked),
//! installs the bundled mod files, then builds everything else from the
//! player's own ROM and game with the mod's own scripts, in the launcher's data
//! root. Steps that are already done are skipped, so a retry resumes.

use crate::config::SetupRecord;
use crate::paths::{self, Bundle};
use sha2::{Digest, Sha256, Sha512};
use crate::system::Group;
use std::io::{BufRead, BufReader, Read, Write};
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

pub const TITLES: [&str; 7] = [
    "Download the helpers",
    "Install the mod",
    "Start Kirby's adventure",
    "Read Kirby's sprites",
    "Collect the copy abilities",
    "Sew Kirby's outfit",
    "Put it all together",
];

/// About how long each step takes, in seconds, for progress and time left:
/// half again what a first setup took on a recent desktop (2026-10-20).
/// Downloads are estimated from their size instead.
const SECONDS: [f32; 7] = [0.0, 2.0, 20.0, 35.0, 35.0, 6.0, 3.0];
const DOWNLOAD_RATE: f32 = 4_000_000.0;
const ABILITIES: [&str; 24] = ["Cutter", "Mirror", "Beam", "Fighter", "Suplex", "YoYo", "Wheel", "Bomb", "Ice", "Fire",
    "Plasma", "Ninja", "Stone", "Wing", "Jet", "Copy", "Sword", "Hammer", "Parasol", "Paint", "Mike", "Crash", "Cook", "Sleep"];

enum Hash {
    Sha256(&'static str),
    Sha512(&'static str),
}

enum Install {
    Zip(&'static str),
    TarGz(&'static str),
    Wheel,
    /// Kept as downloaded, at this path, for a mod script to unpack.
    Place(&'static str),
}

struct Helper {
    name: &'static str,
    file: &'static str,
    url: &'static str,
    hash: Hash,
    size: u64,
    install: Install,
    /// A file that exists once the helper is installed.
    installed: &'static str,
}

/// Pinned downloads. DCCM and the .NET runtime match `scripts/setup_runtime.py`,
/// TurboJPEG matches `scripts/launch_runtime.py`; Pillow and NumPy are the
/// versions the asset tools were verified with.
const LINUX_HELPERS: &[Helper] = &[
    Helper {
        name: "Dead Cells Core Modding 35.13.6",
        file: "linux-x64.zip",
        url: "https://github.com/dead-cells-core-modding/core/releases/download/v35.13.6/linux-x64.zip",
        hash: Hash::Sha256("c57b1fca88e688d228fe5e0aa2b3b4789117f7ff9b092af5c9fd5ada1d3c14a2"),
        size: 28_416_176,
        install: Install::Zip("research/dccm-35.13.6"),
        installed: "research/dccm-35.13.6/core/host/startup/DeadCellsModding",
    },
    Helper {
        name: ".NET runtime 10.0.12",
        file: "dotnet-runtime-10.0.12-linux-x64.tar.gz",
        url: "https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.12/dotnet-runtime-10.0.12-linux-x64.tar.gz",
        hash: Hash::Sha512("58388fdde4f13bd703c7a6f7defb3300b17e42ba5fe8bca50066f80f64ac7406620dcdfb4acc1eff7992750c8cc5cff8369dd12d09730c8a9e8760d6032f7f6e"),
        size: 36_638_192,
        install: Install::TarGz("dotnet"),
        installed: "dotnet/shared/Microsoft.NETCore.App/10.0.12/libcoreclr.so",
    },
    Helper {
        name: "Python 3.14.8",
        file: "cpython-3.14.8+20261009-x86_64-unknown-linux-gnu-install_only.tar.gz",
        url: "https://github.com/astral-sh/python-build-standalone/releases/download/20261009/cpython-3.14.8%2B20261009-x86_64-unknown-linux-gnu-install_only.tar.gz",
        hash: Hash::Sha256("83f9cb480b702548c592443f86209cf0fc03448d90692df257f095c01791dccc"),
        size: 74_948_766,
        install: Install::TarGz("."),
        installed: "python/bin/python3",
    },
    Helper {
        name: "Pillow 12.1.1",
        file: "pillow-12.1.1-cp314-cp314-manylinux2014_x86_64.manylinux_2_17_x86_64.whl",
        url: "https://files.pythonhosted.org/packages/70/f1/f14d5b8eeb4b2cd62b9f9f847eb6605f103df89ef619ac68f92f748614ea/pillow-12.1.1-cp314-cp314-manylinux2014_x86_64.manylinux_2_17_x86_64.whl",
        hash: Hash::Sha256("d6e4571eedf43af33d0fc233a382a76e849badbccdf1ac438841308652a08e1f"),
        size: 8_042_721,
        install: Install::Wheel,
        installed: "python/lib/python3.14/site-packages/PIL/__init__.py",
    },
    Helper {
        name: "NumPy 2.3.5",
        file: "numpy-2.3.5-cp314-cp314-manylinux_2_27_x86_64.manylinux_2_28_x86_64.whl",
        url: "https://files.pythonhosted.org/packages/c8/f6/07ec185b90ec9d7217a00eeeed7383b73d7e709dae2a9a021b051542a708/numpy-2.3.5-cp314-cp314-manylinux_2_27_x86_64.manylinux_2_28_x86_64.whl",
        hash: Hash::Sha256("fffe29a1ef00883599d1dc2c51aa2e5d80afe49523c261a74933df395c15c520"),
        size: 16_597_350,
        install: Install::Wheel,
        installed: "python/lib/python3.14/site-packages/numpy/__init__.py",
    },
    Helper {
        name: "TurboJPEG 2.1.5",
        file: "libturbojpeg0_2.1.5-4ubuntu4_amd64.deb",
        url: "https://archive.ubuntu.com/ubuntu/pool/universe/libj/libjpeg-turbo/libturbojpeg0_2.1.5-4ubuntu4_amd64.deb",
        hash: Hash::Sha256("ea835d7bb9800202058b79118ff27528b48a27a528c46f3a9bff435bbb5f0ed8"),
        size: 196_070,
        install: Install::Place("research/libturbojpeg0_2.1.5-4ubuntu4_amd64.deb"),
        installed: "research/libturbojpeg0_2.1.5-4ubuntu4_amd64.deb",
    },
];

/// The same helpers for Windows (#250). The Windows launch needs no TurboJPEG.
const WINDOWS_HELPERS: &[Helper] = &[
    Helper {
        name: "Dead Cells Core Modding 35.13.6",
        file: "win-x64.zip",
        url: "https://github.com/dead-cells-core-modding/core/releases/download/v35.13.6/win-x64.zip",
        hash: Hash::Sha256("9ceb86e7c79f882ffdb9a7d7a3d39bc0bcc3a16b7bd8a3bd3dc6248bdce131ba"),
        size: 52_237_923,
        install: Install::Zip("research/dccm-35.13.6"),
        installed: "research/dccm-35.13.6/core/host/startup/DeadCellsModding.exe",
    },
    Helper {
        name: ".NET runtime 10.0.12",
        file: "dotnet-runtime-10.0.12-win-x64.zip",
        url: "https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.12/dotnet-runtime-10.0.12-win-x64.zip",
        hash: Hash::Sha512("844fa99e16fd6f44e0a7c29def7a82d7846902334d6a955248a9519a4dddb3f5acceb9c9223bef69f8c83b8ae2417537e5b76dddf79fb7117dc85b5039bc1297"),
        size: 36_960_258,
        install: Install::Zip("dotnet"),
        installed: "dotnet/shared/Microsoft.NETCore.App/10.0.12/coreclr.dll",
    },
    Helper {
        name: "Python 3.14.8",
        file: "cpython-3.14.8+20261009-x86_64-pc-windows-msvc-install_only.tar.gz",
        url: "https://github.com/astral-sh/python-build-standalone/releases/download/20261009/cpython-3.14.8%2B20261009-x86_64-pc-windows-msvc-install_only.tar.gz",
        hash: Hash::Sha256("6c03535bfa5888dd73b0c0ebd91b76a2fdfa06eedaa411b0c4c9e664db6b4bb4"),
        size: 50_154_505,
        install: Install::TarGz("."),
        installed: "python/python.exe",
    },
    Helper {
        name: "Pillow 12.1.1",
        file: "pillow-12.1.1-cp314-cp314-win_amd64.whl",
        url: "https://files.pythonhosted.org/packages/40/72/4c245f7d1044b67affc7f134a09ea619d4895333d35322b775b928180044/pillow-12.1.1-cp314-cp314-win_amd64.whl",
        hash: Hash::Sha256("50480dcd74fa63b8e78235957d302d98d98d82ccbfac4c7e12108ba9ecbdba15"),
        size: 7_176_748,
        install: Install::Wheel,
        installed: "python/Lib/site-packages/PIL/__init__.py",
    },
    Helper {
        name: "NumPy 2.3.5",
        file: "numpy-2.3.5-cp314-cp314-win_amd64.whl",
        url: "https://files.pythonhosted.org/packages/a3/2e/235b4d96619931192c91660805e5e49242389742a7a82c27665021db690c/numpy-2.3.5-cp314-cp314-win_amd64.whl",
        hash: Hash::Sha256("e6a0bc88393d65807d751a614207b7129a310ca4fe76a74e5c7da5fa5671417e"),
        size: 12_919_388,
        install: Install::Wheel,
        installed: "python/Lib/site-packages/numpy/__init__.py",
    },
];

const HELPERS: &[Helper] = if cfg!(windows) { WINDOWS_HELPERS } else { LINUX_HELPERS };
const SITE_PACKAGES: &str = if cfg!(windows) { "python/Lib/site-packages" } else { "python/lib/python3.14/site-packages" };

#[derive(Clone, Copy, Debug, PartialEq)]
pub enum State {
    Busy,
    Done,
    Kept,
}

pub enum Event {
    Step { index: usize, state: State, detail: String, elapsed: Option<Duration> },
    Progress { overall: f32, remaining: Option<Duration> },
    Log(String),
    Failed { index: usize, message: String },
    Cancelled,
    Finished,
}

pub struct Inputs {
    pub bundle: Bundle,
    pub root: PathBuf,
    pub rom: PathBuf,
    pub game: PathBuf,
    /// Build everything again, even what looks done.
    pub repair: bool,
}

/// Lets the window stop a running setup.
#[derive(Clone, Default)]
pub struct Control {
    cancel: Arc<AtomicBool>,
    child: Arc<Mutex<Option<Group>>>,
}

impl Control {
    /// Stops the setup, ending the running script and what it started.
    pub fn cancel(&self) {
        self.cancel.store(true, Ordering::SeqCst);
        if let Some(group) = self.child.lock().unwrap().as_ref() {
            group.interrupt();
        }
    }

    fn cancelled(&self) -> bool {
        self.cancel.load(Ordering::SeqCst)
    }
}

/// The command for one of the mod's Python scripts, with the launcher's
/// runtime and data root, and nothing inherited that could change its imports.
pub fn python_command(python: &Path, project: &Path, root: &Path, rom: Option<&Path>) -> Command {
    let mut command = crate::system::command(python);
    for key in ["PYTHONSTARTUP", "PYTHONSAFEPATH", "PYTHONWARNINGS", "PYTHONINSPECT", "VIRTUAL_ENV", "KSSU_ROM"] {
        command.env_remove(key);
    }
    let path = std::env::var_os("PATH").unwrap_or_default();
    let mut paths = vec![python.parent().unwrap_or(Path::new("/")).to_path_buf()];
    paths.extend(std::env::split_paths(&path));
    command
        .current_dir(project)
        .env("PATH", std::env::join_paths(paths).unwrap_or(path))
        .env("KIRBY_DATA_ROOT", root)
        .env("PYTHONDONTWRITEBYTECODE", "1")
        .env("PYTHONNOUSERSITE", "1")
        .env("PYTHONUNBUFFERED", "1")
        .env("PYTHONUTF8", "1");
    if let Some(rom) = rom {
        command.env("KSSU_ROM", rom);
    }
    command
}

pub fn python(root: &Path) -> PathBuf {
    root.join(if cfg!(windows) { "python/python.exe" } else { "python/bin/python3" })
}

pub fn project(root: &Path) -> PathBuf {
    root.join("app/project")
}

struct Progress {
    index: usize,
    started: Instant,
    expected: f32,
    fraction: Option<f32>,
    detail: String,
}

struct Runner<'a> {
    inputs: &'a Inputs,
    control: &'a Control,
    emit: Arc<dyn Fn(Event) + Send + Sync>,
    log: Mutex<Option<std::fs::File>>,
    progress: Arc<Mutex<Progress>>,
    record: SetupRecord,
    finished: Arc<AtomicBool>,
}

type Failure = String;

impl Runner<'_> {
    fn log(&self, line: &str) {
        if let Some(file) = self.log.lock().unwrap().as_mut() {
            let _ = writeln!(file, "{line}");
        }
        (self.emit)(Event::Log(line.to_string()));
    }

    fn detail(&self, text: impl Into<String>) {
        self.progress.lock().unwrap().detail = text.into();
    }

    fn fraction(&self, value: f32) {
        self.progress.lock().unwrap().fraction = Some(value.clamp(0.0, 1.0));
    }

    fn check_cancel(&self) -> Result<(), Failure> {
        if self.control.cancelled() { Err("cancelled".into()) } else { Ok(()) }
    }

    /// Runs one of the mod's scripts; `watch` sees each output line.
    fn python(&self, args: &[&std::ffi::OsStr], mut watch: impl FnMut(&str)) -> Result<(), Failure> {
        self.check_cancel()?;
        let root = &self.inputs.root;
        let dir = if project(root).is_dir() { project(root) } else { root.clone() };
        let mut command = python_command(&python(root), &dir, root, Some(&self.inputs.rom));
        command.args(args);
        let shown: Vec<String> = args.iter().map(|a| a.to_string_lossy().into_owned()).collect();
        self.log(&format!("$ python3 {}", shown.join(" ")));
        let (reader, writer) = std::io::pipe().map_err(|e| e.to_string())?;
        command.stdin(Stdio::null()).stdout(writer.try_clone().map_err(|e| e.to_string())?).stderr(writer);
        let (mut child, group) = Group::spawn(&mut command).map_err(|e| format!("Python could not start: {e}"))?;
        drop(command);
        *self.control.child.lock().unwrap() = Some(group);
        let mut last = String::new();
        for line in BufReader::new(reader).lines().map_while(Result::ok) {
            self.log(&line);
            watch(&line);
            if !line.trim().is_empty() {
                last = line;
            }
        }
        let status = child.wait().map_err(|e| e.to_string())?;
        *self.control.child.lock().unwrap() = None;
        self.check_cancel()?;
        if status.success() {
            Ok(())
        } else {
            Err(explain_script_error(&last))
        }
    }

    fn download(&self, helper: &Helper, done: &mut u64, total: u64) -> Result<PathBuf, Failure> {
        let dir = self.inputs.root.join("downloads");
        std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
        let path = dir.join(helper.file);
        let partial = dir.join(format!("{}.partial", helper.file));
        let agent = ureq::Agent::config_builder()
            .timeout_connect(Some(Duration::from_secs(30)))
            .timeout_recv_response(Some(Duration::from_secs(60)))
            .timeout_recv_body(Some(Duration::from_secs(30 * 60)))
            .user_agent(concat!("kirbys-dead-land-launcher/", env!("CARGO_PKG_VERSION")))
            .build()
            .new_agent();
        let start = *done;
        let mut last_error = String::new();
        for attempt in 1..=3 {
            self.check_cancel()?;
            *done = start;
            if attempt > 1 {
                self.log(&format!("Trying {} again ({last_error})", helper.name));
                std::thread::sleep(Duration::from_secs(3 * attempt));
            }
            self.log(&format!("Downloading {} from {}", helper.name, helper.url));
            let result = (|| -> Result<String, String> {
                let mut response = agent.get(helper.url).call().map_err(|e| e.to_string())?;
                let mut body = response.body_mut().with_config().limit(helper.size + (1 << 20)).reader();
                let mut file = std::fs::File::create(&partial).map_err(|e| e.to_string())?;
                let (mut sha256, mut sha512) = (Sha256::new(), Sha512::new());
                let mut buffer = vec![0; 1 << 16];
                let mut last_report = Instant::now();
                loop {
                    if self.control.cancelled() {
                        return Err("cancelled".into());
                    }
                    let n = body.read(&mut buffer).map_err(|e| e.to_string())?;
                    if n == 0 {
                        break;
                    }
                    file.write_all(&buffer[..n]).map_err(|e| e.to_string())?;
                    match helper.hash {
                        Hash::Sha256(_) => sha256.update(&buffer[..n]),
                        Hash::Sha512(_) => sha512.update(&buffer[..n]),
                    }
                    *done += n as u64;
                    if last_report.elapsed() > Duration::from_millis(200) {
                        last_report = Instant::now();
                        self.fraction(*done as f32 / total as f32);
                        self.detail(format!("{} · {} of {}", helper.name, megabytes(*done), megabytes(total)));
                    }
                }
                file.sync_all().map_err(|e| e.to_string())?;
                Ok(match helper.hash {
                    Hash::Sha256(_) => hex(&sha256.finalize()),
                    Hash::Sha512(_) => hex(&sha512.finalize()),
                })
            })();
            match result {
                Ok(digest) => {
                    let expected = match helper.hash {
                        Hash::Sha256(d) | Hash::Sha512(d) => d,
                    };
                    if digest != expected {
                        let _ = std::fs::remove_file(&partial);
                        last_error = "the download did not match its pinned hash".into();
                        continue;
                    }
                    std::fs::rename(&partial, &path).map_err(|e| e.to_string())?;
                    return Ok(path);
                }
                Err(error) if error == "cancelled" => {
                    let _ = std::fs::remove_file(&partial);
                    return Err(error);
                }
                Err(error) => last_error = error,
            }
        }
        let _ = std::fs::remove_file(&partial);
        Err(format!("{} could not be downloaded: {last_error}. Check your internet connection and try again.", helper.name))
    }

    fn install(&self, helper: &Helper, archive: &Path) -> Result<(), Failure> {
        let root = &self.inputs.root;
        match helper.install {
            Install::Zip(dir) => {
                let target = root.join(dir);
                remove(&target)?;
                let file = std::fs::File::open(archive).map_err(|e| e.to_string())?;
                let mut zip = zip::ZipArchive::new(file).map_err(|e| e.to_string())?;
                zip.extract(&target).map_err(|e| e.to_string())?;
            }
            Install::TarGz(dir) => {
                let target = if dir == "." { root.join("python") } else { root.join(dir) };
                remove(&target)?;
                let destination = if dir == "." { root.clone() } else { target.clone() };
                std::fs::create_dir_all(&destination).map_err(|e| e.to_string())?;
                let file = std::fs::File::open(archive).map_err(|e| e.to_string())?;
                let mut tar = tar::Archive::new(flate2::read::GzDecoder::new(file));
                tar.set_preserve_permissions(true);
                tar.set_overwrite(true);
                tar.unpack(&destination).map_err(|e| e.to_string())?;
            }
            Install::Wheel => install_wheel(archive, &root.join(SITE_PACKAGES))?,
            Install::Place(path) => {
                let target = root.join(path);
                std::fs::create_dir_all(target.parent().unwrap()).map_err(|e| e.to_string())?;
                std::fs::rename(archive, &target).map_err(|e| e.to_string())?;
            }
        }
        Ok(())
    }

    fn step_downloads(&self) -> Result<bool, Failure> {
        let root = &self.inputs.root;
        let needed = needed_helpers(root, self.inputs.repair);
        if needed.is_empty() {
            return Ok(false);
        }
        let total: u64 = needed.iter().map(|h| h.size).sum();
        let mut done = 0;
        for helper in needed {
            let archive = self.download(helper, &mut done, total)?;
            self.detail(format!("Installing {}", helper.name));
            self.install(helper, &archive).map_err(|e| format!("{} could not be installed: {e}", helper.name))?;
            let _ = std::fs::remove_file(&archive);
        }
        self.fraction(1.0);
        self.detail("Checking Python");
        self.python(&["-c".as_ref(), "import PIL, numpy; print('Pillow', PIL.__version__, 'NumPy', numpy.__version__)".as_ref()], |_| {})?;
        Ok(true)
    }

    fn step_install(&self) -> Result<bool, Failure> {
        let root = &self.inputs.root;
        let bundle = &self.inputs.bundle;
        let stamp = root.join("app/bundle-id");
        let release = root.join(paths::NATIVE_DIR);
        let current = std::fs::read_to_string(&stamp).unwrap_or_default();
        if !self.inputs.repair && current == bundle.id() && project(root).join("scripts/launch_runtime.py").is_file()
            && release.join(paths::BRIDGE).is_file() && release.join(paths::RUNNER).is_file() {
            return Ok(false);
        }
        self.detail(format!("Kirby's Dead Land {}", bundle.version));
        let partial = root.join("app/project.partial");
        remove(&partial)?;
        copy_tree(&bundle.project(), &partial).map_err(|e| format!("The mod files could not be copied: {e}"))?;
        remove(&project(root))?;
        std::fs::rename(&partial, project(root)).map_err(|e| e.to_string())?;
        std::fs::create_dir_all(&release).map_err(|e| e.to_string())?;
        for (source, name) in [(bundle.bridge(), paths::BRIDGE), (bundle.runner(), paths::RUNNER)] {
            let target = release.join(name);
            let _ = std::fs::remove_file(&target);
            std::fs::copy(&source, &target).map_err(|e| format!("{name} could not be copied: {e}"))?;
            set_mode(&target, 0o755)?;
        }
        std::fs::write(&stamp, bundle.id()).map_err(|e| e.to_string())?;
        Ok(true)
    }

    fn step_seed(&self) -> Result<bool, Failure> {
        let root = &self.inputs.root;
        let seed = root.join("oracle/ready2/000.state");
        if seed.is_file() && !self.inputs.repair {
            return Ok(false);
        }
        let plan_path = project(root).join("tools/oracle_bootstrap.json");
        let labels: Vec<String> = std::fs::read(&plan_path)
            .ok()
            .and_then(|b| serde_json::from_slice::<Vec<serde_json::Value>>(&b).ok())
            .unwrap_or_default()
            .iter()
            .map(|step| step["label"].as_str().unwrap_or("").to_string())
            .collect();
        let output = root.join(format!("oracle/launcher-boot-{}", stamp()));
        std::fs::create_dir_all(root.join("oracle")).map_err(|e| e.to_string())?;
        let watch_dir = output.clone();
        let total = labels.len().max(1);
        self.set_probe(move || {
            let states = count(&watch_dir, |name| name.ends_with(".state"));
            let label = labels.get(states.min(total - 1)).cloned().unwrap_or_default();
            (states as f32 / total as f32, format!("Original menus, part {} of {}: {}", (states + 1).min(total), total, label))
        });
        self.python(
            &["tools/oracle_run.py".as_ref(), "--rom".as_ref(), self.inputs.rom.as_os_str(), "--plan".as_ref(),
              "tools/oracle_bootstrap.json".as_ref(), "--output".as_ref(), output.as_os_str()],
            |_| {},
        )?;
        let mut states: Vec<PathBuf> = std::fs::read_dir(&output)
            .map_err(|e| e.to_string())?
            .flatten()
            .map(|e| e.path())
            .filter(|p| p.extension().is_some_and(|e| e == "state"))
            .collect();
        states.sort();
        let last = states.last().ok_or("The original menus wrote no saved state.")?;
        std::fs::create_dir_all(seed.parent().unwrap()).map_err(|e| e.to_string())?;
        std::fs::copy(last, &seed).map_err(|e| e.to_string())?;
        remove(&output)?;
        Ok(true)
    }

    fn step_extract(&self) -> Result<bool, Failure> {
        let root = &self.inputs.root;
        let decoded = root.join("rom/decoded");
        if decoded.join("bkir/manifest.json").is_file() && decoded.join("bkir/frames/0041.png").is_file() && !self.inputs.repair {
            return Ok(false);
        }
        remove(&decoded)?;
        self.python(&["tools/extract_kirby.py".as_ref(), self.inputs.rom.as_os_str()], |line| {
            if let Some(name) = line.split_whitespace().next().filter(|w| w.len() <= 8 && w.chars().all(|c| c.is_ascii_alphanumeric())) {
                self.detail(format!("Decoding {name}"));
            }
        })?;
        Ok(true)
    }

    fn step_icons(&self) -> Result<bool, Failure> {
        let root = &self.inputs.root;
        let icons = root.join("ability-icons");
        if icons.join("manifest.json").is_file() && !self.inputs.repair {
            return Ok(false);
        }
        remove(&icons)?;
        let watch_dir = icons.clone();
        self.set_probe(move || {
            let done = count(&watch_dir, |name| name.ends_with("-32.png"));
            let next = ABILITIES.get(done).copied().unwrap_or("Sleep");
            (done as f32 / 24.0, format!("Ability {} of 24: {next}", (done + 1).min(24)))
        });
        self.python(&["tools/capture_ability_icons.py".as_ref()], |_| {})?;
        Ok(true)
    }

    fn step_outfit(&self) -> Result<bool, Failure> {
        let root = &self.inputs.root;
        let pack = root.join("outfit-build/res.pak");
        let same = self.record.outfit_bundle == self.inputs.bundle.id() && self.record.outfit_game.as_deref() == Some(&self.inputs.game);
        if pack.is_file() && same && !self.inputs.repair {
            return Ok(false);
        }
        // A build stopped by force leaves its lock behind; no other build runs here.
        let _ = std::fs::remove_file(root.join(".outfit-build.build.lock"));
        self.detail("Fitting Kirby into the hero's place");
        self.python(&["tools/build_outfit.py".as_ref(), "--game-pak".as_ref(), self.inputs.game.join("res.pak").as_os_str()], |_| {})?;
        Ok(true)
    }

    fn step_stage(&self) -> Result<bool, Failure> {
        let root = &self.inputs.root;
        let game = &self.inputs.game;
        // The Linux lab links to the game's files; if the game moved, link the new
        // place. On Windows the staging script follows a moved game itself.
        #[cfg(unix)]
        for name in ["hlboot.dat", "res.pak", "steam.hdll", "libsteam_api.so"] {
            let link = root.join("lab").join(name);
            if link.is_symlink() && std::fs::read_link(&link).ok().as_deref() != Some(&game.join(name)) {
                std::fs::remove_file(&link).map_err(|e| e.to_string())?;
            }
        }
        self.detail("Placing the mod beside your game");
        let pack = root.join("outfit-build/res.pak");
        self.python(
            &["scripts/stage_runtime.py".as_ref(), "--original-runtime".as_ref(), "--game".as_ref(), game.as_os_str(),
              "--rom".as_ref(), self.inputs.rom.as_os_str(), "--outfit-pack".as_ref(), pack.as_os_str()],
            |_| {},
        )?;
        // Only the Linux game needs TurboJPEG unpacked.
        if cfg!(unix) {
            self.detail("Unpacking the image library the game needs");
            self.python(&["scripts/launch_runtime.py".as_ref(), "--prepare".as_ref()], |_| {})?;
        }
        Ok(true)
    }

    fn set_probe(&self, probe: impl Fn() -> (f32, String) + Send + 'static) {
        let progress = self.progress.clone();
        let index = progress.lock().unwrap().index;
        let (cancel, finished) = (self.control.cancel.clone(), self.finished.clone());
        std::thread::spawn(move || loop {
            std::thread::sleep(Duration::from_millis(400));
            let (fraction, detail) = probe();
            let mut p = progress.lock().unwrap();
            if p.index != index || cancel.load(Ordering::SeqCst) || finished.load(Ordering::SeqCst) {
                return;
            }
            p.fraction = Some(fraction.min(0.99));
            p.detail = detail;
        });
    }
}

/// The helpers to download: the missing ones, and the wheels again whenever
/// Python itself is replaced, since they live inside it.
fn needed_helpers(root: &Path, repair: bool) -> Vec<&'static Helper> {
    let python_needed = repair || !python(root).is_file();
    HELPERS
        .iter()
        .filter(|h| repair || !root.join(h.installed).is_file() || (python_needed && matches!(h.install, Install::Wheel)))
        .collect()
}

/// Installs a wheel as pip would for these binary packages: its files into
/// site-packages, and any `.data/purelib` or `.data/platlib` beside them.
fn install_wheel(wheel: &Path, site: &Path) -> Result<(), String> {
    let file = std::fs::File::open(wheel).map_err(|e| e.to_string())?;
    let mut zip = zip::ZipArchive::new(file).map_err(|e| e.to_string())?;
    std::fs::create_dir_all(site).map_err(|e| e.to_string())?;
    for i in 0..zip.len() {
        let mut entry = zip.by_index(i).map_err(|e| e.to_string())?;
        let Some(name) = entry.enclosed_name() else { return Err("the wheel holds an unsafe path".into()) };
        let mut parts = name.components();
        let first = parts.next().map(|c| c.as_os_str().to_string_lossy().into_owned()).unwrap_or_default();
        let relative: PathBuf = if first.ends_with(".data") {
            let kind = parts.next().map(|c| c.as_os_str().to_string_lossy().into_owned()).unwrap_or_default();
            if kind != "purelib" && kind != "platlib" {
                continue;
            }
            parts.collect()
        } else {
            name.clone()
        };
        let target = site.join(relative);
        if entry.is_dir() {
            std::fs::create_dir_all(&target).map_err(|e| e.to_string())?;
            continue;
        }
        if let Some(parent) = target.parent() {
            std::fs::create_dir_all(parent).map_err(|e| e.to_string())?;
        }
        let mut out = std::fs::File::create(&target).map_err(|e| e.to_string())?;
        std::io::copy(&mut entry, &mut out).map_err(|e| e.to_string())?;
        if let Some(mode) = entry.unix_mode().filter(|_| cfg!(unix)) {
            set_mode(&target, mode & 0o777)?;
        }
    }
    Ok(())
}

/// Sets a file's Linux permissions; Windows files have none to set.
fn set_mode(path: &Path, mode: u32) -> Result<(), String> {
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        std::fs::set_permissions(path, std::fs::Permissions::from_mode(mode)).map_err(|e| e.to_string())?;
    }
    #[cfg(windows)]
    let _ = (path, mode);
    Ok(())
}

fn remove(path: &Path) -> Result<(), String> {
    match std::fs::symlink_metadata(path) {
        Ok(m) if m.is_dir() => std::fs::remove_dir_all(path).map_err(|e| format!("{} could not be removed: {e}", path.display())),
        Ok(_) => std::fs::remove_file(path).map_err(|e| e.to_string()),
        Err(_) => Ok(()),
    }
}

fn copy_tree(from: &Path, to: &Path) -> std::io::Result<()> {
    std::fs::create_dir_all(to)?;
    for entry in std::fs::read_dir(from)? {
        let entry = entry?;
        let target = to.join(entry.file_name());
        let kind = entry.file_type()?;
        if kind.is_dir() {
            copy_tree(&entry.path(), &target)?;
        } else if kind.is_file() {
            std::fs::copy(entry.path(), &target)?;
        }
    }
    Ok(())
}

fn count(dir: &Path, wanted: impl Fn(&str) -> bool) -> usize {
    std::fs::read_dir(dir)
        .map(|entries| entries.flatten().filter(|e| wanted(&e.file_name().to_string_lossy())).count())
        .unwrap_or(0)
}

fn hex(bytes: &[u8]) -> String {
    bytes.iter().map(|b| format!("{b:02x}")).collect()
}

fn megabytes(bytes: u64) -> String {
    format!("{:.0} MB", bytes as f64 / 1e6)
}

fn stamp() -> String {
    let seconds = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.as_secs()).unwrap_or(0);
    seconds.to_string()
}

/// The last line a failed script printed, as a sentence for the player.
fn explain_script_error(last: &str) -> String {
    let text = last.trim();
    let text = text.strip_prefix("SystemExit: ").unwrap_or(text);
    if text.is_empty() {
        "The step stopped without saying why. Show details for the full log.".into()
    } else if text.contains("MemoryError") || text.contains("Cannot allocate memory") {
        "Your computer ran out of memory. Close other programs and try again.".into()
    } else if text.contains("No space left") {
        "Your disk is full. Free some space and try again.".into()
    } else {
        text.to_string()
    }
}

/// Runs the whole setup on this thread, telling `emit` about each change.
pub fn run(inputs: Inputs, control: Control, emit: Arc<dyn Fn(Event) + Send + Sync>) {
    let root = inputs.root.clone();
    let _ = std::fs::create_dir_all(root.join("logs"));
    let log = std::fs::File::create(root.join("logs/setup.log")).ok();
    let mut record = SetupRecord::load(&root);
    record.finished = false;
    let _ = record.save(&root);
    let needs_downloads: u64 = needed_helpers(&root, inputs.repair).iter().map(|h| h.size).sum();
    let mut weights = SECONDS;
    weights[0] = if needs_downloads > 0 { needs_downloads as f32 / DOWNLOAD_RATE + 10.0 } else { 0.0 };
    let progress = Arc::new(Mutex::new(Progress { index: 0, started: Instant::now(), expected: weights[0], fraction: None, detail: String::new() }));
    // Reports progress and the current detail twice a second.
    let finished = Arc::new(AtomicBool::new(false));
    let runner = Runner {
        inputs: &inputs,
        control: &control,
        emit: emit.clone(),
        log: Mutex::new(log),
        progress: progress.clone(),
        record: record.clone(),
        finished: finished.clone(),
    };
    let completed = Arc::new(Mutex::new((0.0f32, 0.0f32, 0.0f32)));
    {
        let (emit, progress, finished, completed) = (emit.clone(), progress.clone(), finished.clone(), completed.clone());
        let total: f32 = weights.iter().sum();
        std::thread::spawn(move || {
            let mut last_detail = String::new();
            while !finished.load(Ordering::SeqCst) {
                std::thread::sleep(Duration::from_millis(500));
                let p = progress.lock().unwrap();
                if p.index == usize::MAX {
                    continue;
                }
                let elapsed = p.started.elapsed().as_secs_f32();
                // Without a measured fraction, approach the end gently.
                let fraction = p.fraction.unwrap_or_else(|| 0.95 * (1.0 - (-elapsed / p.expected.max(1.0)).exp()));
                let (done_weight, work_expected, work_actual) = *completed.lock().unwrap();
                let current = fraction * p.expected;
                let overall = ((done_weight + current) / total.max(1.0)).min(0.995);
                let speed = if work_expected + current > 20.0 { ((work_actual + elapsed) / (work_expected + current)).clamp(0.4, 4.0) } else { 1.0 };
                let left = (total - done_weight - current).max(0.0) * speed;
                (emit)(Event::Progress { overall, remaining: (work_actual + elapsed > 8.0).then(|| Duration::from_secs_f32(left)) });
                if p.detail != last_detail {
                    last_detail = p.detail.clone();
                    (emit)(Event::Step { index: p.index, state: State::Busy, detail: last_detail.clone(), elapsed: None });
                }
            }
        });
    }

    let steps: [&dyn Fn(&Runner) -> Result<bool, Failure>; 7] = [
        &|r| r.step_downloads(),
        &|r| r.step_install(),
        &|r| r.step_seed(),
        &|r| r.step_extract(),
        &|r| r.step_icons(),
        &|r| r.step_outfit(),
        &|r| r.step_stage(),
    ];
    for (index, step) in steps.iter().enumerate() {
        {
            let mut p = progress.lock().unwrap();
            *p = Progress { index, started: Instant::now(), expected: weights[index].max(1.0), fraction: None, detail: String::new() };
        }
        emit(Event::Step { index, state: State::Busy, detail: String::new(), elapsed: None });
        runner.log(&format!("== {}", TITLES[index]));
        let started = Instant::now();
        let result = step(&runner);
        // Between steps, nothing more is reported for the one that ended.
        progress.lock().unwrap().index = usize::MAX;
        match result {
            Ok(ran) => {
                let elapsed = started.elapsed();
                let mut c = completed.lock().unwrap();
                c.0 += weights[index];
                if ran {
                    c.1 += weights[index];
                    c.2 += elapsed.as_secs_f32();
                }
                drop(c);
                emit(Event::Step { index, state: if ran { State::Done } else { State::Kept }, detail: String::new(), elapsed: ran.then_some(elapsed) });
            }
            Err(error) => {
                finished.store(true, Ordering::SeqCst);
                if control.cancelled() {
                    runner.log("Setup cancelled.");
                    emit(Event::Cancelled);
                } else {
                    runner.log(&format!("Setup stopped: {error}"));
                    emit(Event::Failed { index, message: format!("{} stopped: {error}", TITLES[index]) });
                }
                return;
            }
        }
    }
    finished.store(true, Ordering::SeqCst);
    record.bundle = inputs.bundle.id();
    record.game = Some(inputs.game.clone());
    record.rom = Some(inputs.rom.clone());
    record.outfit_bundle = inputs.bundle.id();
    record.outfit_game = Some(inputs.game.clone());
    record.finished = true;
    if let Err(error) = record.save(&root) {
        emit(Event::Failed { index: 6, message: format!("The setup record could not be saved: {error}") });
        return;
    }
    emit(Event::Progress { overall: 1.0, remaining: None });
    runner.log("Setup finished.");
    emit(Event::Finished);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn helper_pins_are_complete() {
        for helper in LINUX_HELPERS.iter().chain(WINDOWS_HELPERS) {
            let digest = match helper.hash {
                Hash::Sha256(d) => { assert_eq!(d.len(), 64); d }
                Hash::Sha512(d) => { assert_eq!(d.len(), 128); d }
            };
            assert!(digest.chars().all(|c| c.is_ascii_hexdigit()));
            assert!(helper.url.starts_with("https://"));
            assert!(helper.url.ends_with(&helper.file.replace('+', "%2B")), "{}", helper.file);
            assert!(helper.size > 100_000);
        }
    }

    #[test]
    fn helper_pins_match_the_mod_scripts() {
        let scripts = concat!(include_str!("../../scripts/setup_runtime.py"), include_str!("../../scripts/launch_runtime.py"));
        for helper in LINUX_HELPERS.iter().chain(WINDOWS_HELPERS).filter(|h| h.name.starts_with("Dead Cells") || h.name.starts_with("TurboJPEG")) {
            let (Hash::Sha256(digest) | Hash::Sha512(digest)) = helper.hash;
            assert!(scripts.contains(digest), "{} hash differs from the scripts", helper.name);
            // The scripts name DCCM's archive by system, as in 'win-x64' (#246).
            let file = helper.file.strip_suffix(".zip").map(|system| format!("'{system}'")).unwrap_or_else(|| helper.file.to_string());
            assert!(scripts.contains(&file), "{} file differs from the scripts", helper.name);
        }
    }

    #[test]
    fn wheels_install_their_files_and_libraries() {
        let dir = std::env::temp_dir().join(format!("kdl-wheel-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let wheel = dir.join("demo-1.0-py3-none-any.whl");
        {
            let mut zip = zip::ZipWriter::new(std::fs::File::create(&wheel).unwrap());
            let options = zip::write::SimpleFileOptions::default().unix_permissions(0o644);
            for (name, body) in [("demo/__init__.py", "x = 1\n"), ("demo-1.0.dist-info/METADATA", "Name: demo\n"),
                                 ("demo-1.0.data/platlib/demo_native.so", "elf"), ("demo-1.0.data/scripts/demo", "#!/bin/sh\n")] {
                zip.start_file(name, options).unwrap();
                zip.write_all(body.as_bytes()).unwrap();
            }
            zip.finish().unwrap();
        }
        let site = dir.join("site-packages");
        install_wheel(&wheel, &site).unwrap();
        assert_eq!(std::fs::read_to_string(site.join("demo/__init__.py")).unwrap(), "x = 1\n");
        assert!(site.join("demo-1.0.dist-info/METADATA").is_file());
        assert!(site.join("demo_native.so").is_file());
        assert!(!site.join("demo").join("scripts").exists() && !site.join("demo-1.0.data").exists());
        std::fs::remove_dir_all(&dir).unwrap();
    }

    #[test]
    fn script_errors_read_as_sentences() {
        assert_eq!(explain_script_error("SystemExit: The ROM must match"), "The ROM must match");
        assert!(explain_script_error("MemoryError").contains("out of memory"));
        assert!(explain_script_error("").contains("Show details"));
    }

    #[test]
    fn both_systems_get_the_same_helpers() {
        let names = |helpers: &[Helper]| helpers.iter().map(|h| h.name).filter(|n| !n.starts_with("TurboJPEG")).collect::<Vec<_>>();
        assert_eq!(names(LINUX_HELPERS), names(WINDOWS_HELPERS));
        for helper in WINDOWS_HELPERS {
            assert!(!helper.installed.contains("/bin/") && !helper.file.contains("linux"), "{}", helper.file);
        }
        // The wheels go where this system's Python looks for them.
        assert!(HELPERS.iter().filter(|h| matches!(h.install, Install::Wheel)).all(|h| h.installed.starts_with(SITE_PACKAGES)));
    }
}
