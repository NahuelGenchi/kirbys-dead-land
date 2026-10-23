"""Start the Windows game at low integrity, so that it can write only in the lab (#246).

Windows has no bubblewrap. The owner chose this instead (#241): the lab folder is
labeled low integrity, and the game runs with a low-integrity copy of the user's
token, so Windows refuses it every write to a file or folder that is not labeled
low. Files in the lab that are hard links to the game's own keep their label, so
the game cannot write those either. The save backup and the before/after save check
run as on Linux. The game also runs in a job object that closes it when this script
ends, as bubblewrap's --die-with-parent does. With launch_runtime.py's
--normal-integrity it runs in the job with the user's normal token, for a system
where Steam or the graphics driver refuses a low-integrity game.
"""
from __future__ import annotations
import os
from pathlib import Path
import subprocess

LOW = 'S-1-16-4096'
# A low label with no write up; a folder's also passes to what is created in it.
FOLDER_LABEL, FILE_LABEL = 'S:(ML;OICI;NW;;;LW)', 'S:(ML;;NW;;;LW)'
# The Steam client's objects that Steam's overlay opens in the game for XInput and Steam
# Input (#257). Steam makes them at normal integrity, so a low-integrity game cannot open
# them, and the overlay's XInput hook then reads through a null pointer. The owner chose to
# label these, and only these, low (2026-10-22).
STEAM_INPUT_OBJECTS = (
    ('mapping', 'SteamXInput_mem-IPCWrapper'), ('mutex', 'SteamXInput_mutex-IPCWrapper'),
    ('mapping', 'SteamController_Master_mem-IPCWrapper'), ('mapping', 'SteamController_Fast_Master_mem-IPCWrapper'),
    ('mapping', 'SteamController_Slow_Master_mem-IPCWrapper'),
    ('mutex', 'SteamController_Master_Command_mutex-IPCWrapper'),
    ('mutex', 'SteamController_Master_Cursor_Control_mutex-IPCWrapper'),
    ('mutex', 'SteamController_Master_Cursor_Data_mutex-IPCWrapper'),
    ('mutex', 'SteamController_Master_Visualization_Data_mutex-IPCWrapper'),
    ('event', 'SteamController_Master_DataWrittenSteam-IPCWrapper'),
    ('event', 'SteamController_Master_DataWrittenGame-IPCWrapper'),
)


def environment_block(env: dict[str, str]) -> str:
    """A CreateProcess environment block: one entry per name, which Windows compares
    without case (the last one given wins), in the order Windows sorts them."""
    entries = {}
    for name, value in env.items():
        entries[name.upper()] = f'{name}={value}'
    return ''.join(entries[key] + '\0' for key in sorted(entries)) + '\0'


if os.name == 'nt':
    import ctypes
    from ctypes import wintypes
    import msvcrt

    kernel32 = ctypes.WinDLL('kernel32', use_last_error=True)
    advapi32 = ctypes.WinDLL('advapi32', use_last_error=True)
    HANDLE, DWORD, BOOL = wintypes.HANDLE, wintypes.DWORD, wintypes.BOOL
    TOKEN_ASSIGN_PRIMARY, TOKEN_DUPLICATE, TOKEN_QUERY, TOKEN_ADJUST_DEFAULT = 0x1, 0x2, 0x8, 0x80
    SECURITY_IMPERSONATION, TOKEN_PRIMARY, TOKEN_INTEGRITY_LEVEL, SE_GROUP_INTEGRITY = 2, 1, 25, 0x20
    CREATE_SUSPENDED, CREATE_UNICODE_ENVIRONMENT, CREATE_NO_WINDOW, STARTF_USESTDHANDLES = 0x4, 0x400, 0x08000000, 0x100
    JOB_EXTENDED_LIMITS, JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 9, 0x2000
    LABEL_SECURITY_INFORMATION, SDDL_REVISION_1 = 0x10, 1
    WAIT_TIMEOUT, INFINITE = 0x102, 0xFFFFFFFF

    class SID_AND_ATTRIBUTES(ctypes.Structure):
        _fields_ = [('Sid', ctypes.c_void_p), ('Attributes', DWORD)]

    class TOKEN_MANDATORY_LABEL(ctypes.Structure):
        _fields_ = [('Label', SID_AND_ATTRIBUTES)]

    class STARTUPINFOW(ctypes.Structure):
        _fields_ = [('cb', DWORD), ('lpReserved', wintypes.LPWSTR), ('lpDesktop', wintypes.LPWSTR),
                    ('lpTitle', wintypes.LPWSTR), ('dwX', DWORD), ('dwY', DWORD), ('dwXSize', DWORD),
                    ('dwYSize', DWORD), ('dwXCountChars', DWORD), ('dwYCountChars', DWORD),
                    ('dwFillAttribute', DWORD), ('dwFlags', DWORD), ('wShowWindow', wintypes.WORD),
                    ('cbReserved2', wintypes.WORD), ('lpReserved2', ctypes.c_void_p),
                    ('hStdInput', HANDLE), ('hStdOutput', HANDLE), ('hStdError', HANDLE)]

    class PROCESS_INFORMATION(ctypes.Structure):
        _fields_ = [('hProcess', HANDLE), ('hThread', HANDLE), ('dwProcessId', DWORD), ('dwThreadId', DWORD)]

    class JOBOBJECT_BASIC_LIMIT_INFORMATION(ctypes.Structure):
        _fields_ = [('PerProcessUserTimeLimit', ctypes.c_int64), ('PerJobUserTimeLimit', ctypes.c_int64),
                    ('LimitFlags', DWORD), ('MinimumWorkingSetSize', ctypes.c_size_t),
                    ('MaximumWorkingSetSize', ctypes.c_size_t), ('ActiveProcessLimit', DWORD),
                    ('Affinity', ctypes.c_size_t), ('PriorityClass', DWORD), ('SchedulingClass', DWORD)]

    class JOBOBJECT_EXTENDED_LIMIT_INFORMATION(ctypes.Structure):
        _fields_ = [('BasicLimitInformation', JOBOBJECT_BASIC_LIMIT_INFORMATION),
                    ('IoInfo', ctypes.c_ulonglong * 6), ('ProcessMemoryLimit', ctypes.c_size_t),
                    ('JobMemoryLimit', ctypes.c_size_t), ('PeakProcessMemoryUsed', ctypes.c_size_t),
                    ('PeakJobMemoryUsed', ctypes.c_size_t)]

    def _declare(library, name, result, *arguments):
        function = getattr(library, name)
        function.restype, function.argtypes = result, list(arguments)
        return function

    P = ctypes.POINTER
    GetCurrentProcess = _declare(kernel32, 'GetCurrentProcess', HANDLE)
    CloseHandle = _declare(kernel32, 'CloseHandle', BOOL, HANDLE)
    LocalFree = _declare(kernel32, 'LocalFree', ctypes.c_void_p, ctypes.c_void_p)
    CreateJobObjectW = _declare(kernel32, 'CreateJobObjectW', HANDLE, ctypes.c_void_p, wintypes.LPCWSTR)
    SetInformationJobObject = _declare(kernel32, 'SetInformationJobObject', BOOL, HANDLE, ctypes.c_int, ctypes.c_void_p, DWORD)
    AssignProcessToJobObject = _declare(kernel32, 'AssignProcessToJobObject', BOOL, HANDLE, HANDLE)
    ResumeThread = _declare(kernel32, 'ResumeThread', DWORD, HANDLE)
    WaitForSingleObject = _declare(kernel32, 'WaitForSingleObject', DWORD, HANDLE, DWORD)
    GetExitCodeProcess = _declare(kernel32, 'GetExitCodeProcess', BOOL, HANDLE, P(DWORD))
    TerminateProcess = _declare(kernel32, 'TerminateProcess', BOOL, HANDLE, wintypes.UINT)
    OpenProcessToken = _declare(advapi32, 'OpenProcessToken', BOOL, HANDLE, DWORD, P(HANDLE))
    DuplicateTokenEx = _declare(advapi32, 'DuplicateTokenEx', BOOL, HANDLE, DWORD, ctypes.c_void_p, ctypes.c_int, ctypes.c_int, P(HANDLE))
    ConvertStringSidToSidW = _declare(advapi32, 'ConvertStringSidToSidW', BOOL, wintypes.LPCWSTR, P(ctypes.c_void_p))
    GetLengthSid = _declare(advapi32, 'GetLengthSid', DWORD, ctypes.c_void_p)
    SetTokenInformation = _declare(advapi32, 'SetTokenInformation', BOOL, HANDLE, ctypes.c_int, ctypes.c_void_p, DWORD)
    CreateProcessAsUserW = _declare(advapi32, 'CreateProcessAsUserW', BOOL, HANDLE, wintypes.LPCWSTR, ctypes.c_void_p,
                                    ctypes.c_void_p, ctypes.c_void_p, BOOL, DWORD, ctypes.c_void_p, wintypes.LPCWSTR,
                                    P(STARTUPINFOW), P(PROCESS_INFORMATION))
    ConvertSddl = _declare(advapi32, 'ConvertStringSecurityDescriptorToSecurityDescriptorW', BOOL, wintypes.LPCWSTR,
                           DWORD, P(ctypes.c_void_p), ctypes.c_void_p)
    SetFileSecurityW = _declare(advapi32, 'SetFileSecurityW', BOOL, wintypes.LPCWSTR, DWORD, ctypes.c_void_p)
    GetSecurityDescriptorSacl = _declare(advapi32, 'GetSecurityDescriptorSacl', BOOL, ctypes.c_void_p, P(BOOL),
                                         P(ctypes.c_void_p), P(BOOL))
    SetSecurityInfo = _declare(advapi32, 'SetSecurityInfo', DWORD, HANDLE, ctypes.c_int, DWORD, ctypes.c_void_p,
                               ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p)
    OPENERS = {kind: _declare(kernel32, name, HANDLE, DWORD, BOOL, wintypes.LPCWSTR)
               for kind, name in (('mapping', 'OpenFileMappingW'), ('mutex', 'OpenMutexW'), ('event', 'OpenEventW'))}
    SE_KERNEL_OBJECT, WRITE_OWNER, ERROR_FILE_NOT_FOUND = 6, 0x80000, 2

    def _check(result):
        if not result:
            raise ctypes.WinError(ctypes.get_last_error())
        return result

    def label_low(folder: Path) -> int:
        """Label a folder tree low integrity and return how many files were labeled.
        Each object is set on its own, without passing labels down, so a hard-linked
        file (one of the game's), a symbolic link or a junction keeps its label."""
        descriptors = []
        for sddl in (FOLDER_LABEL, FILE_LABEL):
            descriptor = ctypes.c_void_p()
            _check(ConvertSddl(sddl, SDDL_REVISION_1, ctypes.byref(descriptor), None))
            descriptors.append(descriptor)
        files = 0
        try:
            for top, folders, names in os.walk(folder):
                here = Path(top)
                folders[:] = [name for name in folders if not ((here / name).is_symlink() or (here / name).is_junction())]
                _check(SetFileSecurityW(str(here), LABEL_SECURITY_INFORMATION, descriptors[0]))
                for name in names:
                    path = here / name
                    if path.is_symlink() or path.stat().st_nlink > 1:
                        continue
                    _check(SetFileSecurityW(str(path), LABEL_SECURITY_INFORMATION, descriptors[1]))
                    files += 1
        finally:
            for descriptor in descriptors:
                LocalFree(descriptor)
        return files

    def label_steam_input() -> tuple[int, list[str]]:
        """Label the Steam client's input objects (STEAM_INPUT_OBJECTS) low integrity in this
        session. Returns how many were labeled and the ones Windows refused; one Steam has
        not made yet is skipped, since the game then makes it itself, at low integrity."""
        descriptor, sacl = ctypes.c_void_p(), ctypes.c_void_p()
        present, defaulted = BOOL(), BOOL()
        _check(ConvertSddl(FILE_LABEL, SDDL_REVISION_1, ctypes.byref(descriptor), None))
        labeled, refused = 0, []
        try:
            _check(GetSecurityDescriptorSacl(descriptor, ctypes.byref(present), ctypes.byref(sacl), ctypes.byref(defaulted)))
            for kind, name in STEAM_INPUT_OBJECTS:
                handle = OPENERS[kind](WRITE_OWNER, False, name)
                if not handle:
                    if ctypes.get_last_error() != ERROR_FILE_NOT_FOUND:
                        refused.append(name)
                    continue
                try:
                    if SetSecurityInfo(handle, SE_KERNEL_OBJECT, LABEL_SECURITY_INFORMATION, None, None, None, sacl):
                        refused.append(name)
                    else:
                        labeled += 1
                finally:
                    CloseHandle(handle)
        finally:
            LocalFree(descriptor)
        return labeled, refused

    def _token(low: bool):
        """A primary copy of this process's token, at low integrity if low is true."""
        current = HANDLE()
        _check(OpenProcessToken(GetCurrentProcess(), TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ADJUST_DEFAULT,
                                ctypes.byref(current)))
        token = HANDLE()
        try:
            _check(DuplicateTokenEx(current, 0, None, SECURITY_IMPERSONATION, TOKEN_PRIMARY, ctypes.byref(token)))
        finally:
            CloseHandle(current)
        if not low:
            return token
        sid = ctypes.c_void_p()
        try:
            _check(ConvertStringSidToSidW(LOW, ctypes.byref(sid)))
            label = TOKEN_MANDATORY_LABEL(SID_AND_ATTRIBUTES(sid, SE_GROUP_INTEGRITY))
            _check(SetTokenInformation(token, TOKEN_INTEGRITY_LEVEL, ctypes.byref(label),
                                       ctypes.sizeof(label) + GetLengthSid(sid)))
        except BaseException:
            CloseHandle(token)
            raise
        finally:
            LocalFree(sid)
        return token

    class WindowsProcess:
        """The game in a job that closes it with this script, at low integrity unless low is
        false, with the part of subprocess.Popen that launch_runtime.py uses."""

        def __init__(self, command: list[str], env: dict[str, str], cwd: Path, stdout, low: bool = True) -> None:
            self.args, self.returncode = command, None
            output = msvcrt.get_osfhandle(stdout.fileno())
            with open(os.devnull, 'rb') as nothing:
                empty = msvcrt.get_osfhandle(nothing.fileno())
                for handle in (output, empty):
                    os.set_handle_inheritable(handle, True)
                info = STARTUPINFOW(cb=ctypes.sizeof(STARTUPINFOW), dwFlags=STARTF_USESTDHANDLES,
                                    hStdInput=empty, hStdOutput=output, hStdError=output)
                line = ctypes.create_unicode_buffer(subprocess.list2cmdline(command))
                block = environment_block(env)
                block = (ctypes.c_wchar * len(block))(*block)
                created = PROCESS_INFORMATION()
                token = _token(low)
                try:
                    # The game gets a console of its own, with no window: DCCM sets the console's
                    # title and stops when there is none, as when this script has no console.
                    _check(CreateProcessAsUserW(token, None, line, None, None, True,
                                                CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW,
                                                block, str(cwd), ctypes.byref(info), ctypes.byref(created)))
                finally:
                    CloseHandle(token)
                    os.set_handle_inheritable(output, False)
            self._handle, self.pid = created.hProcess, created.dwProcessId
            try:
                self._job = _check(CreateJobObjectW(None, None))
                limits = JOBOBJECT_EXTENDED_LIMIT_INFORMATION()
                limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                _check(SetInformationJobObject(self._job, JOB_EXTENDED_LIMITS, ctypes.byref(limits), ctypes.sizeof(limits)))
                _check(AssignProcessToJobObject(self._job, self._handle))
                if ResumeThread(created.hThread) == 0xFFFFFFFF:
                    raise ctypes.WinError(ctypes.get_last_error())
            except BaseException:
                TerminateProcess(self._handle, 1)
                raise
            finally:
                CloseHandle(created.hThread)

        def poll(self) -> int | None:
            if self.returncode is None and WaitForSingleObject(self._handle, 0) == 0:
                code = DWORD()
                _check(GetExitCodeProcess(self._handle, ctypes.byref(code)))
                self.returncode = code.value
            return self.returncode

        def wait(self, timeout: float | None = None) -> int:
            if WaitForSingleObject(self._handle, INFINITE if timeout is None else int(timeout * 1000)) == WAIT_TIMEOUT:
                raise subprocess.TimeoutExpired(self.args, timeout)
            return self.poll()

        def terminate(self) -> None:
            if self.poll() is None:
                TerminateProcess(self._handle, 1)

        kill = terminate
