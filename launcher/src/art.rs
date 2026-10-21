//! Artwork the software renderer cannot draw itself: soft glows, rounded
//! gradients, progress rings, spinner arcs and the night sky. Each is drawn
//! here as a premultiplied image, antialiased with signed distances, at the
//! window's physical resolution, and cached by its parameters.

use slint::{Color, Image, Rgba8Pixel, SharedPixelBuffer};
use std::cell::{Cell, RefCell};
use std::collections::HashMap;
use std::f32::consts::TAU;

type Px = [f32; 4];

struct Canvas {
    w: usize,
    h: usize,
    px: Vec<Px>,
}

impl Canvas {
    fn new(w: usize, h: usize) -> Self {
        Canvas { w: w.max(1), h: h.max(1), px: vec![[0.0; 4]; w.max(1) * h.max(1)] }
    }

    fn over(&mut self, x: usize, y: usize, c: Px) {
        let d = &mut self.px[y * self.w + x];
        let k = 1.0 - c[3];
        for i in 0..4 {
            d[i] = c[i] + d[i] * k;
        }
    }

    fn image(&self) -> Image {
        let mut buffer = SharedPixelBuffer::<Rgba8Pixel>::new(self.w as u32, self.h as u32);
        for (out, p) in buffer.make_mut_slice().iter_mut().zip(&self.px) {
            let q = |v: f32| (v.clamp(0.0, 1.0) * 255.0 + 0.5) as u8;
            *out = Rgba8Pixel { r: q(p[0]), g: q(p[1]), b: q(p[2]), a: q(p[3]) };
        }
        Image::from_rgba8_premultiplied(buffer)
    }

    pub fn rgba(&self) -> Vec<u8> {
        self.px
            .iter()
            .flat_map(|p| {
                let a = p[3].clamp(0.0, 1.0);
                let un = |v: f32| if a > 0.0 { ((v / a).clamp(0.0, 1.0) * 255.0 + 0.5) as u8 } else { 0 };
                [un(p[0]), un(p[1]), un(p[2]), (a * 255.0 + 0.5) as u8]
            })
            .collect()
    }
}

fn rgb(c: Color) -> [f32; 3] {
    [c.red() as f32 / 255.0, c.green() as f32 / 255.0, c.blue() as f32 / 255.0]
}

fn hex(value: u32) -> Color {
    Color::from_rgb_u8((value >> 16) as u8, (value >> 8) as u8, value as u8)
}

/// A straight color at `alpha` times its own alpha, premultiplied.
fn premul(c: [f32; 3], alpha: f32) -> Px {
    [c[0] * alpha, c[1] * alpha, c[2] * alpha, alpha]
}

fn mix(a: [f32; 3], b: [f32; 3], t: f32) -> [f32; 3] {
    let t = t.clamp(0.0, 1.0);
    [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t]
}

fn smoothstep(e0: f32, e1: f32, x: f32) -> f32 {
    let t = ((x - e0) / (e1 - e0)).clamp(0.0, 1.0);
    t * t * (3.0 - 2.0 * t)
}

/// Signed distance from a point (relative to the center) to a rounded box.
fn rounded_box(x: f32, y: f32, hw: f32, hh: f32, r: f32) -> f32 {
    let r = r.min(hw).min(hh).max(0.0);
    let qx = x.abs() - (hw - r);
    let qy = y.abs() - (hh - r);
    (qx.max(0.0).hypot(qy.max(0.0))) + qx.max(qy).min(0.0) - r
}

fn coverage(distance: f32) -> f32 {
    (0.5 - distance).clamp(0.0, 1.0)
}

/// Pixels per logical pixel, and the images drawn so far.
pub struct Painter {
    pub scale: Cell<f32>,
    cache: RefCell<HashMap<String, Image>>,
}

impl Painter {
    pub fn new() -> Self {
        Painter { scale: Cell::new(1.0), cache: RefCell::new(HashMap::new()) }
    }

    fn cached(&self, key: String, draw: impl FnOnce(f32) -> Image) -> Image {
        if let Some(image) = self.cache.borrow().get(&key) {
            return image.clone();
        }
        let image = draw(self.scale.get());
        let mut cache = self.cache.borrow_mut();
        if cache.len() > 600 {
            cache.clear();
        }
        cache.insert(key, image.clone());
        image
    }

    fn key(&self, kind: &str, values: &[f32], colors: &[Color]) -> String {
        let mut key = format!("{kind}@{:.2}", self.scale.get());
        for v in values {
            key += &format!(":{v:.1}");
        }
        for c in colors {
            key += &format!(":{:08x}", c.as_argb_encoded());
        }
        key
    }

    pub fn glow(&self, w: f32, h: f32, radius: f32, blur: f32, color: Color) -> Image {
        let key = self.key("glow", &[w, h, radius, blur], &[color]);
        self.cached(key, |s| {
            let (iw, ih) = (((w + 2.0 * blur) * s).ceil() as usize, ((h + 2.0 * blur) * s).ceil() as usize);
            let mut canvas = Canvas::new(iw, ih);
            let (cx, cy, hw, hh, r) = (iw as f32 / 2.0, ih as f32 / 2.0, w * s / 2.0, h * s / 2.0, radius * s);
            let sigma = (blur * s / 2.4).max(0.5);
            let (c, alpha) = (rgb(color), color.alpha() as f32 / 255.0);
            for y in 0..ih {
                for x in 0..iw {
                    let d = rounded_box(x as f32 + 0.5 - cx, y as f32 + 0.5 - cy, hw, hh, r);
                    let a = if d <= 0.0 { 1.0 } else { (-0.5 * (d / sigma).powi(2)).exp() };
                    canvas.over(x, y, premul(c, a * alpha));
                }
            }
            canvas.image()
        })
    }

    pub fn pill(&self, w: f32, h: f32, radius: f32, from: Color, to: Color) -> Image {
        let key = self.key("pill", &[w, h, radius], &[from, to]);
        self.cached(key, |s| {
            let (iw, ih) = ((w * s).round().max(1.0) as usize, (h * s).round().max(1.0) as usize);
            let mut canvas = Canvas::new(iw, ih);
            let (hw, hh, r) = (iw as f32 / 2.0, ih as f32 / 2.0, radius * s);
            let (a, b) = (rgb(from), rgb(to));
            let alpha = from.alpha() as f32 / 255.0;
            for y in 0..ih {
                let fy = (y as f32 + 0.5) / ih as f32;
                // A soft shine on the upper half and a slightly deeper lower edge.
                let shine = 0.2 * (1.0 - fy / 0.55).max(0.0).powf(1.6);
                let shade = 0.08 * smoothstep(0.6, 1.0, fy);
                for x in 0..iw {
                    let d = rounded_box(x as f32 + 0.5 - hw, y as f32 + 0.5 - hh, hw, hh, r);
                    let cov = coverage(d);
                    if cov <= 0.0 {
                        continue;
                    }
                    let mut c = mix(a, b, (x as f32 + 0.5) / iw as f32);
                    for v in &mut c {
                        *v = (*v + (1.0 - *v) * shine) * (1.0 - shade);
                    }
                    canvas.over(x, y, premul(c, cov * alpha));
                }
            }
            canvas.image()
        })
    }

    pub fn ring(&self, size: f32, thickness: f32, progress: f32, from: Color, to: Color, track: Color) -> Image {
        let progress = (progress.clamp(0.0, 1.0) * 400.0).round() / 400.0;
        let key = self.key("ring", &[size, thickness, progress * 1000.0], &[from, to, track]);
        self.cached(key, |s| draw_arc(size * s, thickness * s, 0.0, progress * TAU, from, to, Some(track), false))
    }

    pub fn arc(&self, size: f32, thickness: f32, start_degrees: f32, color: Color) -> Image {
        let start = (start_degrees.rem_euclid(360.0) / 6.0).floor() * 6.0;
        let key = self.key("arc", &[size, thickness, start], &[color]);
        self.cached(key, |s| {
            draw_arc(size * s, thickness * s, start.to_radians(), 0.3 * TAU, color, color, None, true)
        })
    }

    pub fn sky(&self, w: f32, h: f32) -> Image {
        let key = self.key("sky", &[w, h], &[]);
        if !self.cache.borrow().contains_key(&key) {
            // Only the current size is worth keeping.
            self.cache.borrow_mut().retain(|k, _| !k.starts_with("sky@"));
        }
        self.cached(key, |s| sky((w * s).round() as usize, (h * s).round() as usize, s).image())
    }
}

/// An arc from `start` (radians clockwise from 12 o'clock) through `sweep`,
/// over an optional full track. A spinner arc fades towards its tail.
#[allow(clippy::too_many_arguments)]
fn draw_arc(size: f32, thickness: f32, start: f32, sweep: f32, from: Color, to: Color, track: Option<Color>, fade: bool) -> Image {
    let n = size.round().max(2.0) as usize;
    let mut canvas = Canvas::new(n, n);
    let c = n as f32 / 2.0;
    let radius = (n as f32 - thickness) / 2.0;
    let half = thickness / 2.0;
    let (a, b) = (rgb(from), rgb(to));
    let alpha = from.alpha() as f32 / 255.0;
    let point = |angle: f32| (c + radius * angle.sin(), c - radius * angle.cos());
    let (sx, sy) = point(start);
    let (ex, ey) = point(start + sweep);
    for y in 0..n {
        for x in 0..n {
            let (px, py) = (x as f32 + 0.5, y as f32 + 0.5);
            let (dx, dy) = (px - c, py - c);
            let band = (dx.hypot(dy) - radius).abs() - half;
            if let Some(track) = track {
                let cov = coverage(band);
                if cov > 0.0 {
                    canvas.over(x, y, premul(rgb(track), cov * track.alpha() as f32 / 255.0));
                }
            }
            if sweep <= 0.0 {
                continue;
            }
            let angle = (dx.atan2(-dy) - start).rem_euclid(TAU);
            let (distance, t) = if angle <= sweep {
                (band, angle / sweep)
            } else {
                let to_end = (px - ex).hypot(py - ey) - half;
                let to_start = if fade { f32::MAX } else { (px - sx).hypot(py - sy) - half };
                if to_end < to_start { (to_end, 1.0) } else { (to_start, 0.0) }
            };
            let cov = coverage(distance);
            if cov <= 0.0 {
                continue;
            }
            let strength = if fade { 0.12 + 0.88 * t * t } else { 1.0 };
            canvas.over(x, y, premul(mix(a, b, t), cov * alpha * strength));
        }
    }
    canvas.image()
}

struct Rng(u64);

impl Rng {
    fn next(&mut self) -> f32 {
        self.0 ^= self.0 << 13;
        self.0 ^= self.0 >> 7;
        self.0 ^= self.0 << 17;
        (self.0 >> 40) as f32 / (1u64 << 24) as f32
    }
}

/// The night sky: a deep gradient, soft nebulae, stars and sparkles, and a
/// castle on the horizon with a few lit windows.
fn sky(w: usize, h: usize, s: f32) -> Canvas {
    let mut canvas = Canvas::new(w, h);
    let (wf, hf) = (w as f32, h as f32);
    let (top, middle, bottom) = (rgb(hex(0x080a24)), rgb(hex(0x121445)), rgb(hex(0x22185a)));
    for y in 0..h {
        let t = y as f32 / hf;
        let base = if t < 0.55 { mix(top, middle, t / 0.55) } else { mix(middle, bottom, (t - 0.55) / 0.45) };
        for x in 0..w {
            canvas.px[y * w + x] = premul(base, 1.0);
        }
    }
    let nebulae = [
        (0.80, 0.14, 0.46, 0xff6fb5, 0.15),
        (0.10, 0.66, 0.40, 0x4f6bff, 0.13),
        (0.48, 1.00, 0.55, 0x9b6bff, 0.12),
        (0.30, 0.05, 0.30, 0x6f8bff, 0.06),
    ];
    for (nx, ny, nr, color, strength) in nebulae {
        let (cx, cy, r, c) = (nx * wf, ny * hf, nr * wf, rgb(hex(color)));
        let (x0, x1) = (((cx - r).max(0.0)) as usize, ((cx + r).min(wf)) as usize);
        let (y0, y1) = (((cy - r).max(0.0)) as usize, ((cy + r).min(hf)) as usize);
        for y in y0..y1 {
            for x in x0..x1 {
                let d = (x as f32 - cx).hypot(y as f32 - cy) / r;
                if d < 1.0 {
                    let a = (1.0 - smoothstep(0.0, 1.0, d)).powi(2) * strength;
                    canvas.over(x, y, premul(c, a));
                }
            }
        }
    }
    let mut rng = Rng(0x9e3779b97f4a7c15);
    let count = (wf * hf / (4300.0 * s * s)) as usize;
    let tints = [rgb(hex(0xffffff)), rgb(hex(0xdcd6ff)), rgb(hex(0xffe9b0)), rgb(hex(0xffd1ea))];
    for i in 0..count {
        let (x, y) = (rng.next() * wf, rng.next().powf(1.25) * hf * 0.86);
        let r = (0.45 + rng.next().powi(3) * 1.1) * s;
        let a = 0.22 + rng.next() * 0.6;
        let tint = tints[if i % 11 == 0 { 2 } else if i % 7 == 0 { 3 } else { (rng.next() * 2.0) as usize }];
        dot(&mut canvas, x, y, r, tint, a);
    }
    // A few sparkles, kept clear of the middle where the screens draw.
    let sparkles = [(0.025, 0.40, 6.0, 0xffcc4d), (0.455, 0.075, 4.5, 0xffffff), (0.66, 0.045, 4.0, 0xffcc4d),
                    (0.975, 0.36, 5.0, 0xffd1ea), (0.30, 0.05, 3.5, 0xffffff), (0.80, 0.085, 3.0, 0xffcc4d)];
    for (sx, sy, size, color) in sparkles {
        sparkle(&mut canvas, sx * wf, sy * hf, size * s, rgb(hex(color)), 0.85);
    }
    castle(&mut canvas, s);
    canvas
}

fn dot(canvas: &mut Canvas, cx: f32, cy: f32, r: f32, c: [f32; 3], a: f32) {
    let reach = r * 3.0;
    let (x0, x1) = ((cx - reach).max(0.0) as usize, ((cx + reach).ceil() as usize).min(canvas.w));
    let (y0, y1) = ((cy - reach).max(0.0) as usize, ((cy + reach).ceil() as usize).min(canvas.h));
    for y in y0..y1 {
        for x in x0..x1 {
            let d = (x as f32 + 0.5 - cx).hypot(y as f32 + 0.5 - cy);
            let v = (-(d / r).powi(2)).exp() * a;
            if v > 0.003 {
                canvas.over(x, y, premul(c, v));
            }
        }
    }
}

/// A four-point star: an astroid with a glowing core.
fn sparkle(canvas: &mut Canvas, cx: f32, cy: f32, size: f32, c: [f32; 3], a: f32) {
    let reach = size * 1.6;
    let (x0, x1) = ((cx - reach).max(0.0) as usize, ((cx + reach).ceil() as usize).min(canvas.w));
    let (y0, y1) = ((cy - reach).max(0.0) as usize, ((cy + reach).ceil() as usize).min(canvas.h));
    for y in y0..y1 {
        for x in x0..x1 {
            let (dx, dy) = (((x as f32 + 0.5 - cx) / size).abs(), ((y as f32 + 0.5 - cy) / size).abs());
            let shape = 1.0 - (dx.sqrt() + dy.sqrt());
            let star = (shape * 3.0).clamp(0.0, 1.0);
            let core = (-(dx * dx + dy * dy) * 9.0).exp() * 0.5;
            let v = (star + core).min(1.0) * a;
            if v > 0.003 {
                canvas.over(x, y, premul(c, v));
            }
        }
    }
}

/// The castle on the horizon, after the Dead Cells ramparts: a far line of
/// hills and spires, then a near wall with crenels, towers and lit windows.
fn castle(canvas: &mut Canvas, s: f32) {
    let (w, h) = (canvas.w, canvas.h);
    let (wf, hf) = (w as f32, h as f32);
    let far = |x: f32| -> f32 {
        let u = x / wf;
        let mut top = hf - s * (58.0 + 18.0 * (u * 5.1 + 0.6).sin() + 9.0 * (u * 12.3 + 2.0).sin());
        for (cu, width, height) in [(0.07, 16.0, 70.0), (0.27, 12.0, 52.0), (0.44, 20.0, 82.0), (0.52, 10.0, 46.0)] {
            let (cx, half) = (cu * wf, width * s / 2.0);
            let dx = (x - cx).abs();
            if dx < half * 1.4 {
                let base = hf - s * (58.0 + height);
                let spire = base - s * height * 0.45 * (1.0 - dx / (half * 1.4));
                top = top.min(if dx < half { spire.min(base) } else { spire });
            }
        }
        top
    };
    let towers = [(0.62, 44.0, 150.0, 40.0), (0.735, 30.0, 112.0, 30.0), (0.865, 60.0, 190.0, 54.0), (0.965, 26.0, 104.0, 24.0)];
    let wall = (0.575, 1.02, 80.0);
    let near = |x: f32| -> f32 {
        let u = x / wf;
        let mut top = hf - s * (28.0 + 9.0 * (u * 3.7 + 1.2).sin() + 4.0 * (u * 9.1).sin());
        if u > wall.0 && u < wall.1 {
            let wall_top = hf - s * wall.2;
            let crenel = ((x / s) % 16.0) < 9.0;
            top = top.min(if crenel { wall_top - s * 9.0 } else { wall_top });
        }
        for (cu, width, height, roof) in towers {
            let (cx, half) = (cu * wf, width * s / 2.0);
            let dx = (x - cx).abs();
            let body = hf - s * height;
            if dx < half {
                top = top.min(body);
            }
            let eave = half + s * 6.0;
            if dx < eave {
                top = top.min(body - s * roof * (1.0 - dx / eave));
            }
        }
        top
    };
    let (far_color, near_color) = (rgb(hex(0x1a1550)), rgb(hex(0x0b0a29)));
    for x in 0..w {
        let (f, n) = (far(x as f32 + 0.5), near(x as f32 + 0.5));
        for y in (f.max(0.0) as usize)..h {
            let cov = (y as f32 + 1.0 - f).clamp(0.0, 1.0);
            canvas.over(x, y, premul(far_color, cov));
        }
        for y in (n.max(0.0) as usize)..h {
            let cov = (y as f32 + 1.0 - n).clamp(0.0, 1.0);
            canvas.over(x, y, premul(near_color, cov));
        }
    }
    let gold = rgb(hex(0xffcc4d));
    let windows = [(0.62, 118.0), (0.62, 84.0), (0.735, 86.0), (0.865, 158.0), (0.865, 120.0), (0.865 + 0.012, 120.0), (0.965, 78.0)];
    for (wu, height) in windows {
        let (cx, cy) = (wu * wf, hf - s * height);
        dot(canvas, cx, cy, 9.0 * s, gold, 0.10);
        let (hw, hh) = (2.6 * s, 4.2 * s);
        for y in ((cy - hh - 1.0).max(0.0) as usize)..((cy + hh + 1.0) as usize).min(h) {
            for x in ((cx - hw - 1.0).max(0.0) as usize)..((cx + hw + 1.0) as usize).min(w) {
                let d = rounded_box(x as f32 + 0.5 - cx, y as f32 + 0.5 - cy, hw, hh, hw * 0.9);
                let cov = coverage(d);
                if cov > 0.0 {
                    canvas.over(x, y, premul(gold, cov * 0.82));
                }
            }
        }
    }
}

/// The launcher's own icon: a gold sparkle on a night tile.
pub fn app_icon(size: usize) -> (Image, Vec<u8>) {
    let mut canvas = Canvas::new(size, size);
    let s = size as f32;
    let (top, bottom) = (rgb(hex(0x1a1d58)), rgb(hex(0x3a1f6e)));
    let half = s / 2.0;
    for y in 0..size {
        for x in 0..size {
            let d = rounded_box(x as f32 + 0.5 - half, y as f32 + 0.5 - half, half - s * 0.03, half - s * 0.03, s * 0.22);
            let cov = coverage(d);
            if cov > 0.0 {
                let c = mix(top, bottom, (x + y) as f32 / (2.0 * s));
                canvas.over(x, y, premul(c, cov));
            }
        }
    }
    dot(&mut canvas, half, half, s * 0.22, rgb(hex(0xff6fb5)), 0.55);
    sparkle(&mut canvas, half, half, s * 0.36, rgb(hex(0xffcc4d)), 1.0);
    for (fx, fy, fs) in [(0.24, 0.26, 0.07), (0.77, 0.73, 0.055)] {
        sparkle(&mut canvas, s * fx, s * fy, s * fs, rgb(hex(0xffffff)), 0.9);
    }
    let image = canvas.image();
    (image, canvas.rgba())
}
