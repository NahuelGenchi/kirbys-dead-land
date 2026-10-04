#!/usr/bin/env python3
"""Encode timestamped private lab framebuffer captures and original sound effects."""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import wave


def read_jsonl(path: Path) -> list[dict]:
    return [json.loads(line) for line in path.read_text().splitlines() if line]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('run', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--ffmpeg', default=shutil.which('ffmpeg'))
    parser.add_argument('--start', type=float, default=0, help='Seconds after the first active gameplay capture.')
    parser.add_argument('--duration', type=float, default=30)
    parser.add_argument('--fps', type=int, default=15)
    args = parser.parse_args()
    if not args.ffmpeg:
        parser.error('Provide --ffmpeg with an installed FFmpeg executable.')
    if args.start < 0 or args.duration <= 0 or not 1 <= args.fps <= 30:
        parser.error('Invalid recording interval or frame rate.')
    run = args.run.resolve()
    frames = read_jsonl(run / 'captures.jsonl')
    active = [row for row in frames if row['gameplay']]
    if not active:
        parser.error('No active gameplay was captured.')
    begin = active[0]['elapsedMs'] / 1000 + args.start
    end = begin + args.duration
    if frames[-1]['elapsedMs'] / 1000 < end:
        parser.error('The requested interval extends beyond the recorded frames.')
    first = max((row for row in frames if row['elapsedMs'] / 1000 <= begin),
                key=lambda row: row['elapsedMs'])
    chosen = [first] + [row for row in frames if begin < row['elapsedMs'] / 1000 < end]
    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    concat = output.with_suffix('.ffconcat')
    lines = ['ffconcat version 1.0']
    for index, row in enumerate(chosen):
        path = run / row['file']
        if not path.is_file() or '\n' in str(path) or "'" in str(path):
            parser.error(f'Unsupported or missing capture path: {path}')
        at = begin if index == 0 else row['elapsedMs'] / 1000
        until = chosen[index + 1]['elapsedMs'] / 1000 if index + 1 < len(chosen) else end
        lines += [f"file '{path}'", f'duration {until - at:.6f}']
    lines.append(f"file '{run / chosen[-1]['file']}'")
    concat.write_text('\n'.join(lines) + '\n')
    command = [str(args.ffmpeg), '-hide_banner', '-loglevel', 'error', '-y',
               '-f', 'concat', '-safe', '0', '-i', str(concat)]
    audio_path = run / 'source-audio.s16le'
    chunks_path = run / 'audio-chunks.jsonl'
    captured_audio = audio_path.is_file() and chunks_path.is_file()
    audio_bytes = 0
    if captured_audio:
        rate = 48000
        pcm = bytearray(round(args.duration * rate) * 4)
        raw = audio_path.read_bytes()
        for chunk in read_jsonl(chunks_path):
            if chunk['rate'] != rate or chunk['channels'] != 2:
                parser.error('Unexpected original PCM format.')
            sample_frames = chunk['bytes'] // 4
            chunk_start = chunk['elapsedMs'] / 1000 - sample_frames / rate
            destination = round((chunk_start - begin) * rate)
            source = chunk['offset']
            count = chunk['bytes']
            if destination < 0:
                skip = min(count, -destination * 4)
                source += skip
                count -= skip
                destination = 0
            offset = destination * 4
            count = min(count, max(0, len(pcm) - offset), max(0, len(raw) - source))
            if count:
                pcm[offset:offset + count] = raw[source:source + count]
                audio_bytes += count
        wav = output.with_suffix('.wav')
        with wave.open(str(wav), 'wb') as writer:
            writer.setnchannels(2)
            writer.setsampwidth(2)
            writer.setframerate(rate)
            writer.writeframes(pcm)
        command += ['-i', str(wav), '-map', '0:v:0', '-map', '1:a:0', '-c:a', 'aac', '-b:a', '160k']
    command += ['-vf', f'fps={args.fps},format=yuv420p', '-c:v', 'libx264',
                '-preset', 'medium', '-crf', '20', '-t', str(args.duration),
                '-movflags', '+faststart', str(output)]
    subprocess.run(command, check=True)
    report = {'video': str(output), 'source_run': str(run), 'start_elapsed_seconds': begin,
              'duration_seconds': args.duration, 'fps': args.fps,
              'recorded_frame_count': len(chosen), 'real_framebuffer_capture': True,
              'original_effects_audio': captured_audio, 'host_audio_captured': False,
              'audio_bytes_used': audio_bytes, 'audio_alignment': 'recorded PCM chunk wall-clock timestamps'}
    output.with_suffix('.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
