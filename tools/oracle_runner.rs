//! Headless original-ROM oracle. Inputs go only to an in-process NDS instance.
//! No desktop window, OS input injection, user save loading, or game-folder access.
use chrono::{TimeZone, Utc};
use melon_rs::{input::ButtonMask, melon::nds::Nds};
use serde_json::{json, Value};
use std::{collections::HashSet, env, fs, io::{BufWriter, Write}, path::Path};

const BASE: usize = 0x02000000;
const END: usize = 0x02400000;
fn u16_at(ram: &[u8], off: usize) -> u16 {
    u16::from_le_bytes(ram[off..off+2].try_into().unwrap())
}
fn u32_at(ram: &[u8], off: usize) -> u32 {
    u32::from_le_bytes(ram[off..off+4].try_into().unwrap())
}
fn entities(ram: &[u8]) -> Value {
    let mut result = Vec::new();
    for slot in 0..7 {
        let mut ptr = u32_at(ram, 0x49dec + 4 * slot) as usize;
        let mut seen = HashSet::new();
        while ptr != 0 {
            if ptr < BASE || ptr + 0x8c > END || ptr % 4 != 0 || !seen.insert(ptr) || seen.len() > 256 {
                return json!({"valid":false,"reason":"candidate entity chain invalid","slot":slot});
            }
            let p = ptr - BASE;
            result.push(json!({"address":ptr,"slot":slot,"kind":ram[p+0x16],
                "parent":u32_at(ram,p+4),"x_raw":u32_at(ram,p+0x80) as i32,
                "y_raw":u32_at(ram,p+0x84) as i32,"vx_raw":u16_at(ram,p+0x88) as i16,
                "vy_raw":u16_at(ram,p+0x8a) as i16}));
            ptr = u32_at(ram,p) as usize;
        }
    }
    json!({"valid":true,"items":result})
}

fn screenshot(nds: &mut Nds, path: &Path, bottom: bool) -> std::io::Result<()> {
    let mut rgba = vec![0u8;256*192*4];
    if !nds.update_framebuffers(&mut rgba,bottom) {
        return Err(std::io::Error::other("framebuffer unavailable"));
    }
    let mut out = BufWriter::new(fs::File::create(path)?);
    out.write_all(b"P6\n256 192\n255\n")?;
    for pixel in rgba.chunks_exact(4) {
        out.write_all(&[pixel[2],pixel[1],pixel[0]])?;
    }
    out.flush()
}

fn main() -> Result<(),Box<dyn std::error::Error>> {
    let args: Vec<String> = env::args().collect();
    if args.len() < 4 || args.len() > 5 {
        return Err("usage: oracle-runner ROM PLAN.json NEW-OUTPUT-DIR [ISOLATED-ORACLE-SAVESTATE]".into());
    }
    let rom = fs::read(&args[1])?;
    if rom.len()!=134217728 || rom.get(12..16)!=Some(b"YKWE") {
        return Err("expected original USA Kirby Super Star Ultra ROM; launcher verifies full hash".into());
    }
    let plan:Value = serde_json::from_slice(&fs::read(&args[2])?)?;
    let steps = plan.as_array().ok_or("plan must be a JSON array")?;
    for step in steps {
        let frames=step["frames"].as_u64().ok_or("each step requires frames")?;
        let mask=step["mask"].as_u64().unwrap_or(0);
        if frames==0 || frames>100000 || mask>4095 {return Err("invalid frames or DS mask".into());}
        if let Some(touch)=step.get("touch") {
            if touch[0].as_u64().is_none_or(|n|n>255) || touch[1].as_u64().is_none_or(|n|n>191) {
                return Err("invalid DS touch point".into());
            }
        }
    }
    let out=Path::new(&args[3]);
    fs::create_dir(out)?; // Refuse overwriting any prior run.
    fs::write(out.join("plan.json"),serde_json::to_vec_pretty(&plan)?)?;
    let mut nds=Nds::new();
    nds.set_nds_cart(&rom,None);
    nds.set_time(Utc.with_ymd_and_hms(2026,10,3,12,0,0).single().unwrap());
    if nds.needs_direct_boot() {nds.setup_direct_boot("KSSU_ORACLE".to_owned());}
    nds.start();
    if args.len()==5 && !nds.read_savestate(args[4].clone()) {return Err("failed to restore isolated state".into());}
    let mut trace=BufWriter::new(fs::File::create(out.join("trace.jsonl"))?);
    let mut elapsed=0u64;
    for (index,step) in steps.iter().enumerate() {
        let count=step["frames"].as_u64().unwrap();
        let mask=step["mask"].as_u64().unwrap_or(0) as u16;
        nds.set_key_mask(ButtonMask::from_bits_retain(mask));
        if let Some(touch)=step.get("touch") {
            nds.touch_screen(touch[0].as_u64().unwrap() as u16,touch[1].as_u64().unwrap() as u16);
        } else {nds.release_screen();}
        for _ in 0..count {
            nds.run_frame();
            let _=nds.read_audio_output(); // Drain internally; never open an audio device.
            elapsed+=1;
            let ram=nds.main_ram();
            let record=json!({"frame":nds.current_frame(),"elapsed":elapsed,"step":index,"mask":mask,
                "label":step["label"],"power_raw":ram[0xbaf7b],"game_raw":ram[0x5b664],
                "part_raw":ram[0x5b665],"map_raw":ram[0x5b666],"rng_raw":u16_at(ram,0x4219c),
                "entities":entities(ram)});
            serde_json::to_writer(&mut trace,&record)?;trace.write_all(b"\n")?;
        }
        screenshot(&mut nds,&out.join(format!("{index:03}-top.ppm")),false)?;
        screenshot(&mut nds,&out.join(format!("{index:03}-bottom.ppm")),true)?;
        fs::write(out.join(format!("{index:03}-ram.bin")),nds.main_ram())?;
        fs::write(out.join(format!("{index:03}.state")),nds.savestate())?;
        trace.flush()?;
        println!("segment {index}: {} frames; total {elapsed}; label {}",count,step["label"]);
    }
    fs::write(out.join("isolated-save.bin"),nds.save_data())?;
    fs::write(out.join("run.json"),serde_json::to_vec_pretty(&json!({
        "schema":"kssu-headless-oracle/v1","rom":args[1],"frames":elapsed,
        "initial_state":args.get(4),"layout_status":"candidate offsets; validate by behavior",
        "melon_rs_commit":"51f25edaf3b2df63f91d049eff8c38fb289150eb",
        "melon_ds_commit":"82fdbc78483f43b310e920e21acc47787cb43564",
        "bios":"bundled FreeBIOS; generated firmware; direct boot",
        "desktop_input_used":false,"gameplay_parity_established":false
    }))?)?;
    Ok(())
}
