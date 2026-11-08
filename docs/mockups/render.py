#!/usr/bin/env python3
"""Render design mockups and package icons with headless Chrome."""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "docs" / "images"
ASSETS = ROOT / "hwmonitor" / "Assets"
MOCK = Path(__file__).resolve().parent
CHROME = next(
    (
        candidate
        for candidate in (
            "/opt/google/chrome/chrome",
            "/usr/local/bin/google-chrome",
            "/usr/bin/google-chrome",
        )
        if Path(candidate).exists()
    ),
    "google-chrome",
)

MARK = """
<svg viewBox="0 0 64 64" fill="none">
  <rect width="64" height="64" rx="16" fill="#0B1220"/>
  <rect x="6" y="6" width="52" height="52" rx="14" fill="#1D4ED8"/>
  <path d="M30 14h4v24a8 8 0 1 1-4 0V14z" fill="#E8F1FF"/>
  <circle cx="32" cy="42" r="5" fill="#67E8F9"/>
  <path d="M44 18h6M44 24h8M44 30h5" stroke="#BFDBFE" stroke-width="2" stroke-linecap="round"/>
</svg>
"""

CSS = (MOCK / "theme.css").read_text()


def page(body: str, width: int, height: int) -> str:
    return f"""<!doctype html>
<html><head><meta charset="utf-8"><style>
html,body{{margin:0;width:{width}px;height:{height}px;overflow:hidden;background:#0e1116}}
{CSS}
</style></head><body>{body}</body></html>"""


def shot(name: str, html: str, width: int, height: int, dest: Path) -> None:
    html_path = MOCK / f"_{name}.html"
    html_path.write_text(page(html, width, height), encoding="utf-8")
    dest.parent.mkdir(parents=True, exist_ok=True)
    png = dest
    subprocess.run(
        [
            "timeout",
            "25",
            CHROME,
            "--headless=new",
            "--no-sandbox",
            "--disable-dev-shm-usage",
            "--disable-gpu",
            "--hide-scrollbars",
            "--force-device-scale-factor=1",
            "--virtual-time-budget=4000",
            f"--user-data-dir=/tmp/chrome-{name}",
            f"--window-size={width},{height}",
            f"--screenshot={png}",
            html_path.as_uri(),
        ],
        check=False,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    if not png.exists() or png.stat().st_size < 500:
        raise SystemExit(f"screenshot failed: {png}")
    html_path.unlink(missing_ok=True)


def main(only: set[str] | None = None) -> None:
    banner = f"""
    <div style="height:100%;padding:64px 72px;background:radial-gradient(900px 420px at 85% 10%, #1d4ed855, transparent 60%), linear-gradient(160deg,#10151f,#0b0e14)">
      <div style="display:flex;align-items:center;gap:18px">
        <div class="mark" style="width:72px;height:72px;border-radius:20px">{MARK}</div>
        <div>
          <div style="font-size:42px;font-weight:700;letter-spacing:-0.6px">HW Monitor</div>
          <div style="color:#b7c0d0;font-size:18px;margin-top:4px">Live CPU and GPU usage and temperature for Command Palette</div>
        </div>
      </div>
      <div style="display:flex;gap:12px;margin-top:48px">
        <div class="chip"><div class="glyph">CPU</div><div><b>27% · 58°C</b><span>CPU</span></div></div>
        <div class="chip"><div class="glyph">iGPU</div><div><b>4% · 58°C</b><span>iGPU</span></div></div>
        <div class="chip"><div class="glyph">GPU</div><div><b style="color:#f97316">58% · 81°C</b><span>GPU</span></div></div>
      </div>
      <div style="margin-top:36px;color:#8b95a7">Dock items and Cool Mode. Design mockup, not a runtime capture.</div>
    </div>"""

    dock = """
    <div style="height:100%;background:linear-gradient(#12343a,#071216);padding:36px 28px">
      <div style="height:64px;border-radius:16px;background:rgba(8,18,22,.86);border:1px solid rgba(255,255,255,.08);display:flex;align-items:center;gap:8px;padding:0 12px">
        <div class="chip"><div class="glyph" style="background:#3b82f6;color:white">CPU</div><div><b>27% · 58°C</b><span>CPU</span></div></div>
        <div class="chip"><div class="glyph" style="background:#3b82f6;color:white">iG</div><div><b>4% · 58°C</b><span>iGPU</span></div></div>
        <div class="chip"><div class="glyph" style="background:#f97316;color:#111827">GPU</div><div><b>58% · 81°C</b><span>GPU</span></div></div>
        <div class="chip"><div class="glyph">C</div><div><b>OFF</b><span>Cool</span></div></div>
      </div>
      <div style="margin-top:28px;color:#d5e7ea;font-size:14px">Command Palette Dock. The value is on the first line and the short label is underneath. Click a sensor to open the list. Click Cool to toggle.</div>
    </div>"""

    sensors = """
    <div style="height:100%;display:grid;place-items:center">
      <div class="palette">
        <div class="top"><div class="mark">""" + MARK + """</div><div style="font-weight:700">HW Monitor</div><div class="search">Search sensors</div></div>
        <div class="section">Cooling</div>
        <div class="row"><div class="glyph">C</div><div><div class="title">Cool Mode</div><div class="sub">Click to limit processor turbo on the active power scheme.</div></div><b>Cool OFF</b></div>
        <div class="section">Processors</div>
        <div class="row"><div class="glyph">CPU</div><div><div class="title">CPU 27% · 58°C</div><div class="sub">Processor · Package</div></div><span class="tag" style="background:#3b82f6;color:white">Cool</span></div>
        <div class="row"><div class="glyph">CPU</div><div><div class="title">Core 2 31% · 61°C</div><div class="sub">Processor · Core 2</div></div><span class="tag" style="background:#eab308;color:#111827">Warm</span></div>
        <div class="section">Graphics</div>
        <div class="row"><div class="glyph">iG</div><div><div class="title">iGPU 4% · 58°C</div><div class="sub">Integrated graphics · Package (shared die)</div></div><span class="tag" style="background:#3b82f6;color:white">Cool</span></div>
        <div class="row"><div class="glyph">GPU</div><div><div class="title">GPU 58% · 81°C</div><div class="sub">Discrete graphics · Adapter</div></div><span class="tag" style="background:#f97316;color:#111827">Hot</span></div>
      </div>
    </div>"""

    settings = """
    <div style="height:100%;display:grid;place-items:center">
      <div class="palette" style="height:1360px">
        <div class="top"><div style="font-weight:700">HW Monitor settings</div></div>
        <div class="section">General</div>
        <div class="field"><label>Refresh interval (ms)</label><p>Applies to the list page and Dock.</p><div class="box">1000</div></div>
        <div class="field toggle"><div><label>Compact sensor list</label><p>Applies to the list page.</p></div><div class="switch off"><i></i></div></div>
        <div class="field toggle"><div><label>Color dock icons</label><p>Applies to the Dock only.</p></div><div class="switch"><i></i></div></div>
        <div class="section">Temperature colors</div>
        <div class="field"><label>Cool below / Warm from / Hot from / Critical from</label><p>Applies to the list page and Dock. Defaults 60 / 60 / 75 / 90 °C.</p><div class="box">60 · 60 · 75 · 90</div></div>
        <div class="field"><label>Cool band color</label><p>Applies to the list page and Dock. Named color, or Custom.</p><div class="box">Blue</div></div>
        <div class="field"><label>Cool custom hex</label><p>Applies to the list page and Dock only when Custom is selected.</p><div class="box">#3B82F6</div></div>
        <div class="section">Cool Mode</div>
        <div class="field"><label>Cool Mode processor cap (%)</label><p>Used by Cool Mode. 99 disables turbo on most processors.</p><div class="box">99</div></div>
        <div class="field toggle"><div><label>Auto Cool Mode</label><p>Used by Cool Mode auto-trigger. On at 85°C for 10s. Off at 70°C for 10s.</p></div><div class="switch off"><i></i></div></div>
        <div class="section">Devices</div>
        <div class="field"><label>CPU short label / full name</label><p>Applies to the list page and Dock. Stored against the hardware id.</p><div class="box">CPU · Processor</div></div>
        <div class="field"><label>CPU in the list / on the dock</label><p>List choice applies to the list page. Dock choice applies to the Dock only.</p><div class="box">Default · Default</div></div>
      </div>
    </div>"""

    cool = """
    <div style="height:100%;display:grid;place-items:center;padding:32px">
      <div class="palette">
        <div class="top"><div class="mark">""" + MARK + """</div><div><div style="font-weight:700">Cool Mode</div><div class="sub">Active power scheme only</div></div></div>
        <div class="row"><div class="glyph">C</div><div><div class="title">Cool ON</div><div class="sub">Maximum processor state is limited. Original AC and DC values are saved and restored on exit.</div></div><span class="tag" style="background:#3b82f6;color:white">99%</span></div>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px;padding:16px 18px 22px">
          <div class="field" style="margin:0"><label>Before</label><div class="box">Max processor state 100%<br>Boost mode Aggressive</div></div>
          <div class="field" style="margin:0"><label>While Cool Mode is on</label><div class="box">Max processor state 99%<br>Boost mode Disabled</div></div>
        </div>
      </div>
    </div>"""

    colors = """
    <div style="height:100%;padding:48px 56px">
      <div style="font-size:28px;font-weight:700">Temperature colors</div>
      <div style="color:#b7c0d0;margin:8px 0 28px">Thresholds and swatches are settings. List tags and dock icons use them.</div>
      <div style="display:grid;grid-template-columns:repeat(4,1fr);gap:16px">
        <div class="field" style="margin:0;border-top:6px solid #3b82f6"><label>Blue</label><p>Below 60°C</p><div class="box">#3B82F6 · Cool</div></div>
        <div class="field" style="margin:0;border-top:6px solid #eab308"><label>Yellow</label><p>From 60°C</p><div class="box">#EAB308 · Warm</div></div>
        <div class="field" style="margin:0;border-top:6px solid #f97316"><label>Orange</label><p>From 75°C</p><div class="box">#F97316 · Hot</div></div>
        <div class="field" style="margin:0;border-top:6px solid #ef4444"><label>Red</label><p>From 90°C</p><div class="box">#EF4444 · Critical</div></div>
      </div>
    </div>"""

    icon = f"""<div style="height:100%;display:grid;place-items:center;background:#0b1220">{MARK.replace('viewBox="0 0 64 64"', 'viewBox="0 0 64 64" width="80%" height="80%"')}</div>"""
    wide = f"""
    <div style="height:100%;display:flex;align-items:center;gap:28px;padding:0 48px;background:linear-gradient(120deg,#0b1220,#1d4ed8)">
      <div style="width:120px">{MARK}</div>
      <div><div style="font-size:42px;font-weight:720">HW Monitor</div><div style="opacity:.8">Command Palette</div></div>
    </div>"""

    shots = [
        ("banner", banner, 1280, 420, OUT / "banner.png"),
        ("dock", dock, 1280, 220, OUT / "dock.png"),
        ("sensors", sensors, 1040, 760, OUT / "sensors.png"),
        ("settings", settings, 1040, 1460, OUT / "settings.png"),
        ("cool-mode", cool, 1040, 520, OUT / "cool-mode.png"),
        ("color-levels", colors, 1100, 360, OUT / "color-levels.png"),
        ("logo", icon, 512, 512, OUT / "logo.png"),
    ]
    for name, html, w, h, dest in shots:
        if only and name not in only:
            continue
        shot(name, html, w, h, dest)
        print("wrote", dest)

    if only:
        return

    icon_sizes = {
        "StoreLogo.png": (50, 50),
        "Square44x44Logo.png": (44, 44),
        "Square44x44Logo.scale-200.png": (88, 88),
        "Square44x44Logo.targetsize-24_altform-unplated.png": (24, 24),
        "Square150x150Logo.png": (150, 150),
        "Square150x150Logo.scale-200.png": (300, 300),
        "LockScreenLogo.scale-200.png": (48, 48),
    }
    for filename, (w, h) in icon_sizes.items():
        shot(filename.replace(".", "-"), icon, w, h, ASSETS / filename)
    shot("wide", wide, 310, 150, ASSETS / "Wide310x150Logo.png")
    shot("wide-200", wide, 620, 300, ASSETS / "Wide310x150Logo.scale-200.png")
    shot("splash", wide, 620, 300, ASSETS / "SplashScreen.png")
    shot("splash-200", wide, 620, 300, ASSETS / "SplashScreen.scale-200.png")
    print("assets ready")


if __name__ == "__main__":
    chosen = {name for name in sys.argv[1:] if name}
    main(chosen or None)
