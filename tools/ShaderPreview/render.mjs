// Renders preview frames of the shared atmosphere/cloud shader in headless Chromium (WebGL2).
// Usage: node render.mjs <outDir> <dataDir>   (dataDir from `SimTool export`, shaders from build.sh)
import { createRequire } from 'module';
const require = createRequire(import.meta.url);
const { chromium } = require('playwright'); // pre-installed globally in CI images; npm i playwright otherwise
import fs from 'fs';
import path from 'path';

const outDir = process.argv[2] || 'out';
const dataDir = process.argv[3] || 'data';
const W = +(process.env.W || 480), H = +(process.env.H || 270);
const frag = fs.readFileSync(path.join(outDir, 'preview.frag'), 'utf8');
const refl = JSON.parse(fs.readFileSync(path.join(outDir, 'preview.json'), 'utf8'));
const block = Object.values(refl.types).find(t => t.members && t.members.some(m => m.name === '_PlanetRadius'));
const layout = Object.fromEntries(block.members.map(m => [m.name, m]));
const blockFloats = Math.ceil(refl.ubos[0].block_size / 16) * 4;
const shape = fs.readFileSync(path.join(dataDir, 'shape64.bin')).toString('base64');
const coverage = fs.readFileSync(path.join(dataDir, 'coverage256.bin')).toString('base64');
const levels = JSON.parse(fs.readFileSync(path.join(dataDir, 'levels.json'), 'utf8'));

const R = 6371000;
const sun = norm([0.45, 0.58, -0.68]);
function norm(v) { const l = Math.hypot(...v); return v.map(x => x / l); }
function cross(a, b) { return [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]; }
function altitude(p) { const d = [p[0], p[1] + R, p[2]]; return (p[0]*p[0]+p[1]*p[1]+p[2]*p[2] + 2*R*p[1]) / (Math.hypot(...d) + R); }

// Scenarios: camera position (local frame), look direction, level, time.
const scenarios = process.env.SC ? JSON.parse(process.env.SC) : [
  { name: '01-orbit-release', level: 0, pos: [0, 120000, -150000], look: [0, -0.32, 1], fov: 62 },
  { name: '02-upper-atmosphere', level: 0, pos: [0, 60000, -60000], look: [0.1, -0.55, 1], fov: 62 },
  { name: '03-above-cumulus', level: 2, pos: [0, 9000, -12000], look: [0.3, -0.25, 1], fov: 62 },
  { name: '04-cloud-top-skim', level: 2, pos: [1500, 5600, -6000], look: [0.2, -0.12, 1], fov: 62 },
  { name: '05-inside-cloud', level: 2, pos: [0, 3300, -3000], look: [0.0, -0.4, 1], fov: 62 },
  { name: '06-below-deck', level: 2, pos: [0, 700, -2500], look: [0.4, 0.15, 1], fov: 62 },
  { name: '07-storm-front', level: 3, pos: [8000, 7000, -20000], look: [-0.45, -0.08, 1], fov: 62 },
  { name: '08-ocean-final', level: 5, pos: [0, 900, -3000], look: [0.0, -0.3, 1], fov: 62 },
  { name: '09-looking-sunward', level: 1, pos: [0, 4000, -8000], look: [0.45, 0.25, -0.68], fov: 62 },
];

function buildUniforms(sc) {
  const f = new Float32Array(blockFloats);
  const set = (name, vals) => { const m = layout[name]; if (!m) return; const o = m.offset / 4; (Array.isArray(vals) ? vals : [vals]).forEach((v, i) => f[o + i] = v); };
  const setArr = (name, arr) => { const m = layout[name]; if (!m) return; arr.forEach((v4, i) => v4.forEach((v, j) => f[m.offset / 4 + i * (m.array_stride / 4) + j] = v)); };
  const L = levels[sc.level];
  const fwd = norm(sc.look);
  const up0 = norm([sc.pos[0], sc.pos[1] + R, sc.pos[2]]);
  const right = norm(cross(up0, fwd)).map(x => -x); // left-handed screen right
  const upv = cross(fwd, right).map(x => -x);
  set('_PlanetRadius', R); set('_AtmoTop', 110000); set('_CamAlt', altitude(sc.pos)); set('_CamUp', up0);
  set('_CamLocal', sc.pos); set('_SunDir', sun); set('_SunIntensity', 20); set('_Exposure', +(process.env.EXPOSURE || 0.6));
  const layers = L.layers.map(l => [l[0], l[1], l[2], l[3]]);
  const layersB = L.layers.map(l => [l[4], l[5], 0, 0]);
  setArr('_CloudLayerA', layers); setArr('_CloudLayerB', layersB); set('_CloudLayerCount', L.layers.length);
  setArr('_StormCells', L.storms); set('_StormCount', L.storms.length);
  set('_CloudDrift', [0, 0]); set('_CoverageTile', 36000); set('_ExtinctionScale', 0.03);
  set('_CloudSteps', +(process.env.STEPS || 48)); set('_LightSteps', 4); set('_AtmoSteps', 12);
  set('_Flash', [0, 0, 0, 0]); set('_FrameJitter', 0);
  set('_CloudBandMin', Math.min(...L.layers.map(l => l[0]))); set('_CloudBandMax', Math.max(...L.layers.map(l => l[1])));
  set('_OceanWorld', L.ocean);
  set('_Resolution', [W, H]); set('_CamRight', right); set('_CamUpV', upv); set('_CamFwd', fwd);
  set('_TanHalfFov', Math.tan(sc.fov * Math.PI / 360));
  set('_Debug', +(sc.debug || process.env.DEBUG || 0));
  return Array.from(f);
}

const html = `<!doctype html><html><body style="margin:0;background:#000"><canvas id="c" width="${W}" height="${H}"></canvas><script>
window.FRAG = ${JSON.stringify(frag)}; window.SHAPE = "${shape}"; window.COVER = "${coverage}";
const gl = document.getElementById('c').getContext('webgl2', {preserveDrawingBuffer: true});
function b64(s){ const b = atob(s); const a = new Uint8Array(b.length); for (let i=0;i<b.length;i++) a[i]=b.charCodeAt(i); return a; }
function sh(type, src){ const s = gl.createShader(type); gl.shaderSource(s, src); gl.compileShader(s); if(!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s)); return s; }
const vs = '#version 300 es\\nin vec2 p; void main(){ gl_Position = vec4(p,0.,1.); }';
const prog = gl.createProgram(); gl.attachShader(prog, sh(gl.VERTEX_SHADER, vs)); gl.attachShader(prog, sh(gl.FRAGMENT_SHADER, window.FRAG)); gl.linkProgram(prog);
if(!gl.getProgramParameter(prog, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(prog));
gl.useProgram(prog);
const buf = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, buf); gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1,-1,3,-1,-1,3]), gl.STATIC_DRAW);
const loc = gl.getAttribLocation(prog,'p'); gl.enableVertexAttribArray(loc); gl.vertexAttribPointer(loc,2,gl.FLOAT,false,0,0);
gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
const t3 = gl.createTexture(); gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_3D, t3);
gl.texImage3D(gl.TEXTURE_3D, 0, gl.R8, 64, 64, 64, 0, gl.RED, gl.UNSIGNED_BYTE, b64(window.SHAPE));
for (const p of [gl.TEXTURE_WRAP_S, gl.TEXTURE_WRAP_T, gl.TEXTURE_WRAP_R]) gl.texParameteri(gl.TEXTURE_3D, p, gl.REPEAT);
gl.texParameteri(gl.TEXTURE_3D, gl.TEXTURE_MIN_FILTER, gl.LINEAR); gl.texParameteri(gl.TEXTURE_3D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
const t2 = gl.createTexture(); gl.activeTexture(gl.TEXTURE1); gl.bindTexture(gl.TEXTURE_2D, t2);
gl.texImage2D(gl.TEXTURE_2D, 0, gl.R8, 256, 256, 0, gl.RED, gl.UNSIGNED_BYTE, b64(window.COVER));
gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.REPEAT);
gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
for (const u of gl.getProgramParameter(prog, gl.ACTIVE_UNIFORMS) ? [...Array(gl.getProgramParameter(prog, gl.ACTIVE_UNIFORMS)).keys()] : []) {
  const info = gl.getActiveUniform(prog, u);
  if (info.name.includes('Shape')) gl.uniform1i(gl.getUniformLocation(prog, info.name), 0);
  if (info.name.includes('Coverage')) gl.uniform1i(gl.getUniformLocation(prog, info.name), 1);
}
window.renderFrame = (u) => { gl.uniform4fv(gl.getUniformLocation(prog, '_Global'), new Float32Array(u)); gl.viewport(0,0,${W},${H}); gl.drawArrays(gl.TRIANGLES,0,3); gl.finish(); return true; };
window.READY = true;
</script></body></html>`;

const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
const page = await browser.newPage({ viewport: { width: W, height: H } });
page.on('console', m => console.log('page:', m.text()));
page.on('pageerror', e => console.log('page error:', e.message));
await page.setContent(html);
await page.waitForFunction(() => window.READY === true, null, { timeout: 120000 });
const only = process.env.ONLY;
for (const sc of scenarios) {
  if (only && !sc.name.includes(only)) continue;
  const t0 = Date.now();
  await page.evaluate((u) => window.renderFrame(u), buildUniforms(sc));
  await page.screenshot({ path: path.join(outDir, sc.name + '.png') });
  console.log(sc.name, (Date.now() - t0) + ' ms');
}
await browser.close();
