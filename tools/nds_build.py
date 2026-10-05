#!/usr/bin/env python3
"""Build the original-ROM bridge against pinned private oracle dependencies."""
import hashlib,json,os,subprocess
from pathlib import Path
from oracle_build import ROOT,WRAPPER,CORE,BRIDGE,REPAIRS,git_output
from nds_vendor import GPU_EDITS,check_gpu_patch
from nds_graphics_patch import GRAPHICS_WRAPPER_FILES,GRAPHICS_CORE_FILES,check_graphics_patch

def main():
    vendor=ROOT/'vendor/melon-rs'
    if git_output(vendor,'rev-parse','HEAD')!=WRAPPER or git_output(vendor/'melonDS','rev-parse','HEAD')!=CORE:
        raise SystemExit('Unexpected source revisions; run tools/oracle_build.py first.')
    changed=git_output(vendor,'diff','--name-only').splitlines()
    if any(path not in (BRIDGE,'melonDS',*GPU_EDITS,*GRAPHICS_WRAPPER_FILES) for path in changed):
        raise SystemExit('Unexpected wrapper changes; left untouched.')
    if any(path not in GRAPHICS_CORE_FILES for path in git_output(vendor/'melonDS','diff','--name-only').splitlines()):
        raise SystemExit('Unexpected core changes; left untouched.')
    original=subprocess.check_output(['git','-C',str(vendor),'show',f'{WRAPPER}:{BRIDGE}'],text=True)
    repaired=original
    for before,after in REPAIRS:repaired=repaired.replace(before,after)
    if (vendor/BRIDGE).read_text()!=repaired:raise SystemExit('Required checked camera shim repairs missing; run tools/oracle_build.py.')
    check_graphics_patch(vendor,WRAPPER,CORE,apply=True)
    check_gpu_patch(vendor,WRAPPER,apply=True)
    crate=ROOT/'bridge';crate.mkdir(exist_ok=True)
    source=Path(__file__).resolve().parents[1]/'src/nds/lib.rs'
    (crate/'Cargo.toml').write_text('[package]\nname="kssu-bridge"\nversion="0.1.0"\nedition="2021"\n[lib]\ncrate-type=["cdylib"]\npath='+json.dumps(str(source))+'\n[dependencies]\nmelon-rs={path="../vendor/melon-rs",default-features=false}\nchrono="=0.4.45"\nsha2="=0.10.9"\n')
    subprocess.run(['cargo','build','--release','--manifest-path',str(crate/'Cargo.toml')],env=dict(os.environ,CARGO_TARGET_DIR=str(vendor/'target')),check=True)
    binary=vendor/'target/release/libkssu_bridge.so'
    provenance={'melon_rs':WRAPPER,'melon_ds':CORE,'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'binary_sha256':hashlib.sha256(binary.read_bytes()).hexdigest(),'cargo_lock_sha256':hashlib.sha256((crate/'Cargo.lock').read_bytes()).hexdigest(),'binary':str(binary)}
    (ROOT/'bridge-provenance.json').write_text(json.dumps(provenance,indent=2)+'\n')
    print(binary)
if __name__=='__main__':main()
