#!/usr/bin/env python3
"""Build the original-ROM bridge against pinned private oracle dependencies."""
import argparse,hashlib,json,subprocess
from pathlib import Path
from oracle_build import ROOT,WRAPPER,CORE,BRIDGE,REPAIRS,TARGETS,bridge_manifest,bridge_binary,cargo_build,git_output,provenance_file,windows_report
from nds_vendor import GPU_EDITS,check_gpu_patch
from nds_graphics_patch import GRAPHICS_WRAPPER_FILES,GRAPHICS_CORE_FILES,check_graphics_patch
from nds_call_patch import CALL_CORE_FILES,check_call_patch
from nds_step_patch import STEP_CORE_FILES,check_step_patch
from nds_jit_patch import JIT_WRAPPER_FILES,JIT_CORE_FILES,check_jit_patch

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--target',choices=TARGETS,default='linux',help='Build the bridge for Linux, or cross-build kssu_bridge.dll for Windows (#242).')
    args=parser.parse_args()
    vendor=ROOT/'vendor/melon-rs'
    if git_output(vendor,'rev-parse','HEAD')!=WRAPPER or git_output(vendor/'melonDS','rev-parse','HEAD')!=CORE:
        raise SystemExit('Unexpected source revisions; run tools/oracle_build.py first.')
    changed=git_output(vendor,'diff','--name-only').splitlines()
    if any(path not in (BRIDGE,'melonDS',*GPU_EDITS,*GRAPHICS_WRAPPER_FILES,*JIT_WRAPPER_FILES) for path in changed):
        raise SystemExit('Unexpected wrapper changes; left untouched.')
    if any(path not in (*GRAPHICS_CORE_FILES,*CALL_CORE_FILES,*STEP_CORE_FILES,*JIT_CORE_FILES) for path in git_output(vendor/'melonDS','diff','--name-only').splitlines()):
        raise SystemExit('Unexpected core changes; left untouched.')
    original=subprocess.check_output(['git','-C',str(vendor),'show',f'{WRAPPER}:{BRIDGE}'],text=True)
    repaired=original
    for before,after in REPAIRS:repaired=repaired.replace(before,after)
    if (vendor/BRIDGE).read_text()!=repaired:raise SystemExit('Required checked camera shim repairs missing; run tools/oracle_build.py.')
    check_graphics_patch(vendor,WRAPPER,CORE,apply=True)
    check_call_patch(vendor,CORE,apply=True)
    check_step_patch(vendor,CORE,apply=True)
    check_jit_patch(vendor,WRAPPER,CORE,apply=True)
    check_gpu_patch(vendor,WRAPPER,apply=True)
    crate=ROOT/'bridge';crate.mkdir(exist_ok=True)
    source=Path(__file__).resolve().parents[1]/'src/nds/lib.rs'
    (crate/'Cargo.toml').write_text(bridge_manifest(source))
    cargo_build(crate/'Cargo.toml',vendor,args.target)
    binary=bridge_binary(vendor,args.target)
    provenance={'melon_rs':WRAPPER,'melon_ds':CORE,'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'binary_sha256':hashlib.sha256(binary.read_bytes()).hexdigest(),'cargo_lock_sha256':hashlib.sha256((crate/'Cargo.lock').read_bytes()).hexdigest(),'binary':str(binary)}
    if args.target=='windows':provenance.update(windows_report(binary))
    provenance_file('bridge',args.target).write_text(json.dumps(provenance,indent=2)+'\n')
    print(binary)
if __name__=='__main__':main()
