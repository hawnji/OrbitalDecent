#!/usr/bin/env python3
"""Type-checks every CGPROGRAM in Assets/Resources/Shaders with glslang (HLSL front end) using stub
Unity includes. Catches syntax and type errors without the Unity editor; it does not validate
Unity-specific semantics. Usage: python3 tools/ShaderCheck/check.py"""
import os, re, subprocess, sys, tempfile
here = os.path.dirname(os.path.abspath(__file__))
shaders = os.path.normpath(os.path.join(here, '..', '..', 'Assets', 'Resources', 'Shaders'))
stubs = os.path.join(here, 'stubs')
fail = 0
for name in sorted(os.listdir(shaders)):
    if not name.endswith('.shader'): continue
    src = open(os.path.join(shaders, name)).read()
    inc = re.findall(r'CGINCLUDE(.*?)ENDCG', src, re.S)
    progs = re.findall(r'CGPROGRAM(.*?)ENDCG', src, re.S)
    for idx, body in enumerate(progs):
        code = (inc[0] if inc else '') + body
        code = re.sub(r'^\s*#pragma.*$', '', code, flags=re.M)
        vs = re.search(r'#pragma\s+vertex\s+(\w+)', body).group(1)
        fs = re.search(r'#pragma\s+fragment\s+(\w+)', body).group(1)
        with tempfile.NamedTemporaryFile('w', suffix='.hlsl', dir=shaders, delete=False) as f:
            f.write(code); tmp = f.name
        try:
            for stage, entry in (('vert', vs), ('frag', fs)):
                r = subprocess.run(['glslangValidator', '-D', '-V', '--hlsl-dx9-compatible', '-S', stage, '-e', entry,
                                    '-I' + stubs, '-I' + shaders, '--auto-map-bindings', '--auto-map-locations',
                                    '-o', os.devnull, tmp], capture_output=True, text=True)
                ok = r.returncode == 0
                print(f"{'ok  ' if ok else 'FAIL'} {name} pass {idx} {stage} ({entry})")
                if not ok:
                    fail += 1
                    print('\n'.join(l for l in r.stdout.splitlines() if 'ERROR' in l or 'error' in l)[:3000])
        finally:
            os.unlink(tmp)
sys.exit(1 if fail else 0)
