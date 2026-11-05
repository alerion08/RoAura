#!/usr/bin/env python3
"""Deterministic RoAura tutorial step caption renderer.

Requires Python >=3.10, Pillow and (for video) OpenCV and ffmpeg.
Montserrat font files are NOT distributed. Resolve from installed fonts or use --font-*.

Examples:
  python tools/render.py --preview /path/to/frame.png --output examples/preview.png --step 4
  python tools/render.py --video /path/to/recording.mp4 --output examples/demo.gif --max-seconds 10
  python tools/render.py --video /path/to/recording.mp4 --output examples/demo.mp4
"""
from __future__ import annotations

import argparse
import json
import math
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
STYLE_DEFAULT = ROOT / 'template' / 'style.json'
PROJECT_DEFAULT = ROOT / 'examples' / 'add-effect.json'


def load_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8'))


def resolve_fonts(extra_bold=None, medium=None):
    windir = os.getenv('WINDIR', 'C:/Windows')
    userfonts = Path(os.getenv('LOCALAPPDATA', '')) / 'Microsoft' / 'Windows' / 'Fonts'
    font_dirs = [
        Path(os.getenv('ROAURA_FONTS_DIR', '/__absent__')),
        ROOT / 'local_fonts',
        userfonts,
        Path(windir) / 'Fonts',
        Path('/usr/share/texlive/texmf-dist/fonts/opentype/public/montserrat'),
        Path('/usr/local/share/fonts'),
        Path('/usr/share/fonts/truetype/montserrat')
    ]
    choices = {
        'ExtraBold': (extra_bold, ('Montserrat-ExtraBold.otf', 'Montserrat-ExtraBold.ttf', 'Montserrat ExtraBold.ttf')),
        'Medium': (medium, ('Montserrat-Medium.otf', 'Montserrat-Medium.ttf', 'Montserrat Medium.ttf'))
    }
    out = {}
    for style, (explicit, names) in choices.items():
        candidates = [Path(explicit)] if explicit else [d / n for d in font_dirs for n in names]
        matching = next((p for p in candidates if p.is_file()), None)
        if matching is None:
            raise FileNotFoundError(
                f'Montserrat {style} not found. Install Montserrat or provide --font-'
                f'{"extrabold" if style == "ExtraBold" else "medium"} PATH. '
                f'Original font files are not included in this style kit.'
            )
        out[style] = matching
    return out


def text_width(draw, text, font):
    return draw.textlength(text, font=font)


def center_text(draw, xy, text, font, fill):
    draw.text(xy, text, font=font, fill=fill, anchor='mm')


def wordwrap(draw, text, font, max_width):
    lines = []
    for para in text.split('\n'):
        current = ''
        for word in para.split():
            test = (current + ' ' + word).strip()
            if text_width(draw, test, font) <= max_width:
                current = test
            else:
                if current:
                    lines.append(current)
                current = word
        lines.append(current)
    return lines


def fitting_lines(draw, text, base_font_size, min_font_size, max_width, max_lines, font_path):
    for size in range(base_font_size, min_font_size - 1, -1):
        font = ImageFont.truetype(font_path, size)
        lines = wordwrap(draw, text, font, max_width)
        if len(lines) <= max_lines and all(text_width(draw, line, font) <= max_width for line in lines):
            return font, lines, size
    raise ValueError('Action caption is too long for the approved template; shorten it or split into steps: ' + text)


def compose(frame, title, description, number, total, style, fonts):
    """Draw reproducible text and shapes on original frames, without inventing interface elements."""
    frame = frame.convert('RGB')
    w, h = frame.size
    base_w, base_h = style['reference_size']
    ratio = w / base_w
    if abs((w / h) - (base_w / base_h)) > 0.08:
        raise ValueError(f'Unexpected video aspect ratio: {w}x{h}; template is {base_w}:{base_h}. Crop source first.')
    ss = style['rendering']['supersampling']
    factor = ratio * ss
    img = frame.resize((w * ss, h * ss), Image.Resampling.LANCZOS).convert('RGBA')
    d = ImageDraw.Draw(img)
    colors = style['colors']

    def rgb(name):
        return tuple(bytes.fromhex(colors[name].lstrip('#')))

    def sc(value):
        return round(value * factor)

    tp = style['typography']
    if 'header' in style:
        hp = style['header']
        head_font = ImageFont.truetype(fonts['ExtraBold'], sc(tp['title']['size']))
        title_len = text_width(d, title, head_font) / factor
        title_w = min(hp['max_width'], max(hp['min_width'], hp['text_x'] - hp['x'] + title_len + hp['inner_right_padding']))
        if title_w >= hp['max_width'] and title_len > hp['max_width'] - (hp['text_x'] - hp['x']) - hp['inner_right_padding']:
            raise ValueError('Instruction title does not fit the template: ' + title)
        x1, y1 = sc(hp['x']), sc(hp['y'])
        x2, y2 = sc(hp['x'] + title_w), sc(hp['y'] + hp['height'])
        d.rounded_rectangle((x1, y1, x2, y2), radius=sc(hp['radius']), fill=rgb('panel_background'), outline=rgb('border'), width=max(1, sc(hp['border_width'])))
        d.line((sc(hp['divider_x']), sc(hp['divider_y1']), sc(hp['divider_x']), sc(hp['divider_y2'])), fill=rgb('divider'), width=max(1, sc(hp['divider_width'])))
        d.text((sc(hp['text_x']), sc(hp['y'] + hp['height'] / 2)), title, font=head_font, fill=rgb('title_text'), anchor='lm')

    fp = style['footer']
    fx, fy = sc(fp['x']), h * ss - sc(fp['bottom_margin'] + fp['height'])
    fr = w * ss - sc(fp['right_margin'])
    fb = h * ss - sc(fp['bottom_margin'])
    d.rounded_rectangle((fx, fy, fr, fb), radius=sc(fp['radius']), fill=rgb('panel_background'), outline=rgb('border'), width=max(1, sc(fp['border_width'])))
    cy = (fy + fb) // 2
    cx = sc(fp['badge_center_x'])
    r = sc(fp['badge_radius'])
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=rgb('badge_background'))
    label_font = ImageFont.truetype(fonts['ExtraBold'], sc(tp['badge']['size'] if len(str(number)) <= 2 else 24))
    center_text(d, (cx, cy), str(number), label_font, rgb('badge_text'))
    divx = sc(fp['divider_x'])
    d.line((divx, fy + sc(fp['divider_top_offset']), divx, fb - sc(fp['divider_bottom_offset'])), fill=rgb('divider'), width=max(1, sc(fp['divider_width'])))

    text_left = sc(fp['text_x'])
    text_right = w * ss - sc(fp['text_right_margin'])
    max_width = text_right - text_left
    desc_font, lines, px = fitting_lines(
        d, description, sc(tp['description']['size']), sc(tp['description']['min_size']),
        max_width, tp['description']['max_lines'], fonts['Medium'])
    line_h = round(px * 1.27)
    full_h = line_h * len(lines) + sc(fp['line_spacing']) * (len(lines) - 1)
    first_cy = cy - full_h / 2 + line_h / 2
    for i, line in enumerate(lines):
        text_y = round(first_cy + i * (line_h + sc(fp['line_spacing'])))
        d.text((text_left, text_y), line, font=desc_font, fill=rgb('description_text'), anchor='lm')
    return img.convert('RGB').resize((w, h), Image.Resampling.LANCZOS)


def step_for_time(project, t):
    steps = project['steps']
    for i, step in enumerate(steps):
        if step['start'] <= t < step['end']:
            return i + 1, step
    if t >= steps[-1]['end']:
        return len(steps), steps[-1]
    return 1, steps[0]


def resize_frame(frame, width):
    if width is None or frame.width == width:
        return frame
    height = round(frame.height * width / frame.width)
    if height % 2:
        height += 1
    return frame.resize((width, height), Image.Resampling.LANCZOS)


def render_video(args, style, project, fonts):
    try:
        import cv2
    except ImportError as exc:
        raise RuntimeError('Please install opencv-python for video mode') from exc
    if not shutil.which('ffmpeg'):
        raise RuntimeError('ffmpeg is required to render video/GIF')
    cap = cv2.VideoCapture(str(args.video))
    if not cap.isOpened():
        raise ValueError('Cannot open video: ' + str(args.video))
    native_fps = cap.get(cv2.CAP_PROP_FPS) or 30
    frame_total = cap.get(cv2.CAP_PROP_FRAME_COUNT) or 0
    duration = frame_total / native_fps
    fps = args.fps or style['rendering']['fps']
    start = max(0, args.start)
    end = min(duration, start + (args.max_seconds if args.max_seconds is not None else duration))
    if end <= start:
        raise ValueError('No frames in the requested time range')
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    width = args.width or style['rendering']['default_width']
    if width % 2:
        width += 1
    out_h = round(cap.get(cv2.CAP_PROP_FRAME_HEIGHT) * width / cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    if out_h % 2:
        out_h += 1
    if output.suffix.lower() == '.mp4':
        stream_target = output
    else:
        fd, tmp = tempfile.mkstemp(suffix='.mp4')
        os.close(fd)
        stream_target = Path(tmp)
    command = [
        'ffmpeg', '-hide_banner', '-loglevel', 'error', '-y',
        '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', f'{width}x{out_h}',
        '-r', str(fps), '-i', 'pipe:0', '-an',
        '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '20', str(stream_target)
    ]
    proc = subprocess.Popen(command, stdin=subprocess.PIPE, stderr=subprocess.PIPE)
    rendered = 0
    try:
        for j in range(math.ceil((end - start) * fps)):
            t = start + j / fps
            cap.set(cv2.CAP_PROP_POS_MSEC, t * 1000)
            ok, frame = cap.read()
            if not ok:
                break
            frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            frame = Image.fromarray(frame)
            frame = resize_frame(frame, width)
            if frame.size != (width, out_h):
                frame = frame.resize((width, out_h), Image.Resampling.LANCZOS)
            n, step = step_for_time(project, t)
            result = compose(frame, project.get('title', ''), step['text'], n, len(project['steps']), style, fonts)
            proc.stdin.write(result.tobytes())
            rendered += 1
    finally:
        cap.release()
        if proc.stdin:
            proc.stdin.close()
        rc = proc.wait()
        if rc != 0:
            err = proc.stderr.read().decode('utf-8', errors='replace')
            raise RuntimeError('ffmpeg failed: ' + err[:2000])
        proc.stderr.close()
    if rendered == 0:
        raise ValueError('No frames decoded')
    if output.suffix.lower() == '.gif':
        fd, tmp = tempfile.mkstemp(suffix='.png')
        os.close(fd)
        pal = Path(tmp)
        try:
            subprocess.run(['ffmpeg', '-y', '-hide_banner', '-loglevel', 'error', '-i', str(stream_target), '-vf', 'palettegen=stats_mode=diff', str(pal)], check=True)
            subprocess.run(['ffmpeg', '-y', '-hide_banner', '-loglevel', 'error', '-i', str(stream_target), '-i', str(pal), '-filter_complex', '[0:v][1:v]paletteuse=dither=bayer:bayer_scale=3', '-loop', '0', str(output)], check=True)
        finally:
            pal.unlink(missing_ok=True)
            stream_target.unlink(missing_ok=True)
    print(f'{output}  |  {rendered} frames at {fps} fps  |  {width}x{out_h}')


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawTextHelpFormatter)
    ap.add_argument('--style', type=Path, default=STYLE_DEFAULT)
    ap.add_argument('--project', type=Path, default=PROJECT_DEFAULT)
    sources = ap.add_mutually_exclusive_group(required=True)
    sources.add_argument('--preview', type=Path, help='PNG/JPG frame to decorate')
    sources.add_argument('--video', type=Path, help='MP4 video to decorate')
    ap.add_argument('--output', type=Path, required=True)
    ap.add_argument('--step', type=int, default=2, help='Step number for --preview')
    ap.add_argument('--width', type=int, default=None)
    ap.add_argument('--fps', type=int, default=None)
    ap.add_argument('--start', type=float, default=0)
    ap.add_argument('--max-seconds', type=float, default=None)
    ap.add_argument('--font-extrabold', default=None)
    ap.add_argument('--font-medium', default=None)
    args = ap.parse_args()
    style = load_json(args.style)
    project = load_json(args.project)
    fonts = resolve_fonts(args.font_extrabold, args.font_medium)
    if args.preview:
        im = Image.open(args.preview).convert('RGB')
        im = resize_frame(im, args.width)
        step = min(max(args.step, 1), len(project['steps']))
        output = compose(im, project.get('title', ''), project['steps'][step - 1]['text'], step, len(project['steps']), style, fonts)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        output.save(args.output)
        print(f'{args.output} | preview {output.width}x{output.height}')
    else:
        if args.output.suffix.lower() not in ('.gif', '.mp4'):
            ap.error('Video output must be .gif or .mp4')
        render_video(args, style, project, fonts)


if __name__ == '__main__':
    main()
