//! The Windows calls behind the launcher's system questions (#250): the
//! registry, the process list, free memory, the shell, drive permissions and
//! job objects. Linux answers the same questions in `system.rs` and `steam.rs`.

use std::ffi::OsStr;
use std::os::windows::ffi::{OsStrExt, OsStringExt};
use std::path::{Path, PathBuf};
use windows_sys::Win32::Foundation::{CloseHandle, HANDLE, INVALID_HANDLE_VALUE};
use windows_sys::Win32::Storage::FileSystem::{GetVolumeInformationW, GetVolumePathNameW};
use windows_sys::Win32::System::Console::{AttachConsole, ATTACH_PARENT_PROCESS};
use windows_sys::Win32::System::Diagnostics::ToolHelp::{CreateToolhelp32Snapshot, Process32FirstW, Process32NextW, PROCESSENTRY32W, TH32CS_SNAPPROCESS};
use windows_sys::Win32::System::JobObjects::{
    AssignProcessToJobObject, CreateJobObjectW, JobObjectExtendedLimitInformation, SetInformationJobObject, TerminateJobObject,
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION, JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
};
use windows_sys::Win32::System::Registry::{RegGetValueW, HKEY_CURRENT_USER, RRF_RT_REG_SZ};
use windows_sys::Win32::System::SystemInformation::{GlobalMemoryStatusEx, MEMORYSTATUSEX};
use windows_sys::Win32::System::SystemServices::FILE_PERSISTENT_ACLS;
use windows_sys::Win32::UI::Shell::ShellExecuteW;
use windows_sys::Win32::UI::WindowsAndMessaging::SW_SHOWNORMAL;

/// What `CreateProcess` gets for every script the launcher starts: a console of
/// its own that never shows, as the launcher has none to share.
pub const CREATE_NO_WINDOW: u32 = windows_sys::Win32::System::Threading::CREATE_NO_WINDOW;

fn wide(text: impl AsRef<OsStr>) -> Vec<u16> {
    text.as_ref().encode_wide().chain(Some(0)).collect()
}

fn unwide(buffer: &[u16]) -> PathBuf {
    let end = buffer.iter().position(|&c| c == 0).unwrap_or(buffer.len());
    PathBuf::from(std::ffi::OsString::from_wide(&buffer[..end]))
}

/// A text value under `HKEY_CURRENT_USER`.
pub fn user_registry_text(key: &str, value: &str) -> Option<String> {
    let (key, value) = (wide(key), wide(value));
    let mut buffer = vec![0u16; 1024];
    let mut size = (buffer.len() * 2) as u32;
    let status = unsafe {
        RegGetValueW(HKEY_CURRENT_USER, key.as_ptr(), value.as_ptr(), RRF_RT_REG_SZ, std::ptr::null_mut(), buffer.as_mut_ptr().cast(), &mut size)
    };
    (status == 0).then(|| unwide(&buffer).to_string_lossy().into_owned()).filter(|text| !text.is_empty())
}

/// Whether a program with this file name (such as `steam.exe`) is running.
pub fn process_running(name: &str) -> bool {
    let snapshot = unsafe { CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0) };
    if snapshot == INVALID_HANDLE_VALUE {
        return false;
    }
    let mut entry = PROCESSENTRY32W { dwSize: std::mem::size_of::<PROCESSENTRY32W>() as u32, ..Default::default() };
    let mut found = false;
    let mut more = unsafe { Process32FirstW(snapshot, &mut entry) } != 0;
    while more && !found {
        found = unwide(&entry.szExeFile).to_string_lossy().eq_ignore_ascii_case(name);
        more = unsafe { Process32NextW(snapshot, &mut entry) } != 0;
    }
    unsafe { CloseHandle(snapshot) };
    found
}

/// Free physical memory, as Windows reports it to Task Manager.
pub fn memory_available() -> Option<u64> {
    let mut status = MEMORYSTATUSEX { dwLength: std::mem::size_of::<MEMORYSTATUSEX>() as u32, ..Default::default() };
    (unsafe { GlobalMemoryStatusEx(&mut status) } != 0).then_some(status.ullAvailPhys)
}

/// Opens a folder, file or address (`https:`, `steam:`) the way Explorer would.
pub fn shell_open(target: &str) -> std::io::Result<()> {
    // Explorer wants a path's backslashes; addresses keep their slashes.
    let target = if target.contains("://") { target.to_string() } else { target.replace('/', "\\") };
    let (verb, target) = (wide("open"), wide(target));
    let result = unsafe { ShellExecuteW(std::ptr::null_mut(), verb.as_ptr(), target.as_ptr(), std::ptr::null(), std::ptr::null(), SW_SHOWNORMAL) };
    // Values above 32 mean success; the others are error codes.
    if result as usize > 32 { Ok(()) } else { Err(std::io::Error::other(format!("Windows could not open it (error {})", result as usize))) }
}

/// The file system of the drive holding `path` (or the nearest folder above it
/// that exists), and whether it keeps Windows permissions, as NTFS does.
pub fn drive_permissions(path: &Path) -> Option<(String, bool)> {
    let existing = path.ancestors().find(|p| p.exists())?;
    let mut volume = vec![0u16; 1024];
    if unsafe { GetVolumePathNameW(wide(existing).as_ptr(), volume.as_mut_ptr(), volume.len() as u32) } == 0 {
        return None;
    }
    let (mut flags, mut system) = (0u32, vec![0u16; 64]);
    let ok = unsafe {
        GetVolumeInformationW(volume.as_ptr(), std::ptr::null_mut(), 0, std::ptr::null_mut(), std::ptr::null_mut(), &mut flags, system.as_mut_ptr(), system.len() as u32)
    };
    (ok != 0).then(|| (unwide(&system).to_string_lossy().into_owned(), flags & FILE_PERSISTENT_ACLS != 0))
}

/// Lets `--check` and `--help` print to the terminal that started the launcher,
/// which a Windows GUI program is not given.
pub fn attach_parent_console() {
    unsafe { AttachConsole(ATTACH_PARENT_PROCESS) };
}

/// A job object that holds a script and every program it starts, and ends them
/// all when it is terminated or closed.
pub struct Job(HANDLE);

// The handle is only used through these calls, which Windows makes thread-safe.
unsafe impl Send for Job {}
unsafe impl Sync for Job {}

impl Job {
    pub fn holding(process: HANDLE) -> std::io::Result<Job> {
        let job = unsafe { CreateJobObjectW(std::ptr::null(), std::ptr::null()) };
        if job.is_null() {
            return Err(std::io::Error::last_os_error());
        }
        let job = Job(job);
        let mut limits = JOBOBJECT_EXTENDED_LIMIT_INFORMATION::default();
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        let size = std::mem::size_of_val(&limits) as u32;
        if unsafe { SetInformationJobObject(job.0, JobObjectExtendedLimitInformation, (&limits as *const JOBOBJECT_EXTENDED_LIMIT_INFORMATION).cast(), size) } == 0
            || unsafe { AssignProcessToJobObject(job.0, process) } == 0
        {
            return Err(std::io::Error::last_os_error());
        }
        Ok(job)
    }

    pub fn terminate(&self) {
        unsafe { TerminateJobObject(self.0, 1) };
    }
}

impl Drop for Job {
    fn drop(&mut self) {
        unsafe { CloseHandle(self.0) };
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn finds_running_programs_by_file_name() {
        let me = std::env::current_exe().unwrap();
        assert!(process_running(&me.file_name().unwrap().to_string_lossy().to_uppercase()));
        assert!(!process_running("no-such-program.exe"));
    }

    #[test]
    fn reads_the_drive_of_a_folder_still_to_be_made() {
        let (system, _) = drive_permissions(&std::env::temp_dir().join("kdl-not-made-yet").join("lab")).unwrap();
        assert!(!system.is_empty());
    }
}
