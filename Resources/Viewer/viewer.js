// ============================================================
// VRM Viewer — 最简版
// 只负责：加载模型 + 渲染 + 接收 C# 事件驱动表情
// 无自动动画、无自动表情循环、无骨骼 idle
// ============================================================

import {
  CAMERA, LIGHTS, RENDERER,
  MOOD_MAP, STATE_EFFECTS,
} from './viewer-config.js';

import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { VRMLoaderPlugin, VRMUtils } from 'https://cdn.jsdelivr.net/npm/@pixiv/three-vrm@2.1.3/lib/three-vrm.module.min.js';

// ---- DOM ----
const statusEl = document.getElementById('status');
const infoEl   = document.getElementById('info');

function showError(html) {
  statusEl.className = 'error';
  statusEl.style.opacity = '1';
  statusEl.innerHTML = html;
}
function esc(s) { return String(s || '').replace(/</g, '&lt;'); }

// ---- URL 参数 ----
const params    = new URLSearchParams(location.search);
const modelPath = params.get('model');
if (!modelPath) { showError('<div>Missing ?model= parameter</div>'); throw new Error('no model'); }

const base   = params.get('base') || 'https://vrm.local/';
const vrmUrl = base + modelPath.split('/').map(s => encodeURIComponent(s)).join('/');
statusEl.innerHTML = '<div class="spinner"></div><div>Loading...</div>';

// ============================================================
// Three.js 场景
// ============================================================

const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, premultipliedAlpha: false });
renderer.setPixelRatio(Math.min(window.devicePixelRatio, RENDERER.MAX_PIXEL_RATIO));
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = RENDERER.TONE_MAPPING_EXPOSURE;
renderer.setClearColor(RENDERER.CLEAR_COLOR, RENDERER.CLEAR_ALPHA);
document.body.appendChild(renderer.domElement);

const scene = new THREE.Scene();
scene.background = null;

const camera = new THREE.PerspectiveCamera(CAMERA.FOV, window.innerWidth / window.innerHeight, CAMERA.NEAR, CAMERA.FAR);
camera.position.set(...CAMERA.POSITION);

const controls = new OrbitControls(camera, renderer.domElement);
controls.target.set(...CAMERA.TARGET);
controls.enableDamping = true;
controls.dampingFactor = CAMERA.DAMPING_FACTOR;
controls.minDistance = CAMERA.MIN_DISTANCE;
controls.maxDistance = CAMERA.MAX_DISTANCE;
controls.maxPolarAngle = CAMERA.MAX_POLAR_ANGLE;
controls.enablePan = false;
controls.enableRotate = false;
controls.enableZoom = false;
controls.update();

scene.add(new THREE.AmbientLight(LIGHTS.AMBIENT.color, LIGHTS.AMBIENT.intensity));
const dir1 = new THREE.DirectionalLight(LIGHTS.DIR_1.color, LIGHTS.DIR_1.intensity);
dir1.position.set(...LIGHTS.DIR_1.position);
scene.add(dir1);
const dir2 = new THREE.DirectionalLight(LIGHTS.DIR_2.color, LIGHTS.DIR_2.intensity);
dir2.position.set(...LIGHTS.DIR_2.position);
scene.add(dir2);

// ============================================================
// VRM 状态
// ============================================================

let vrm = null;
let currentExpression = null;  // 当前激活的表情名

// ============================================================
// 表情工具
// ============================================================

const exprMgr = () => vrm && (vrm.expressionManager || vrm.blendShapeProxy);

/** 直接写 morph target（绕过 expressionManager.update） */
function applyExpr(name, weight) {
  if (!vrm) return;
  const em = exprMgr();
  if (!em) return;
  try {
    // 读取 expression 的 binds，直接写 morph target
    const expr = em._expressionMap?.[name];
    if (expr?.binds) {
      for (const bind of expr.binds) {
        if (!bind?.mesh?.morphTargetInfluences || bind.index == null) continue;
        bind.mesh.morphTargetInfluences[bind.index] = (bind.weight ?? 1) * weight;
      }
    }
  } catch (_) {}
}

/** 清除指定表情的 morph target */
function clearExpr(name) {
  applyExpr(name, 0);
}

/** 清除所有表情 morph target */
function clearAllExpr() {
  if (!vrm) return;
  vrm.scene.traverse(obj => {
    if (obj.morphTargetInfluences) {
      for (let i = 0; i < obj.morphTargetInfluences.length; i++) obj.morphTargetInfluences[i] = 0;
    }
  });
}

// ---- 公开 API（C# 调用） ----

window.setExpression = function(name, weight) {
  // 先清旧表情
  if (currentExpression && currentExpression !== name) clearExpr(currentExpression);
  currentExpression = name;
  applyExpr(name, weight);
};

window.resetExpressions = function() {
  clearAllExpr();
  currentExpression = null;
};

// ============================================================
// C# → JS 消息通道
// ============================================================

function handleMessage(msg) {
  if (!msg || !msg.type) return;
  switch (msg.type) {
    case 'mood':    handleMood(msg.expression); break;
    case 'state':   handleState(msg.state); break;
    case 'opacity': document.body.style.opacity = msg.value; break;
  }
}

function handleMood(expression) {
  if (!vrm) return;
  const expr = MOOD_MAP[(expression || '').toLowerCase()];
  if (!expr) return;

  if (expr === 'neutral') {
    window.resetExpressions();
  } else {
    window.setExpression(expr, 0.8);
  }
}

function handleState(state) {
  if (!vrm || !state) return;
  const effect = STATE_EFFECTS[state.toLowerCase()];
  if (!effect) return;

  if (effect.reset) {
    window.resetExpressions();
    return;
  }

  window.resetExpressions();
  if (effect.expression) {
    window.setExpression(effect.expression, 0.8);
  }
}

if (window.chrome?.webview) {
  window.chrome.webview.addEventListener('message', e => handleMessage(e.data));
}

// ============================================================
// VRM 加载
// ============================================================

const loader = new GLTFLoader();
loader.register(parser => new VRMLoaderPlugin(parser));

try {
  const gltf = await loader.loadAsync(vrmUrl);
  vrm = gltf.userData.vrm;
  if (!vrm) { showError('<div>No VRM extension</div>'); throw new Error('no vrm'); }

  VRMUtils.rotateVRM0(vrm);
  scene.add(vrm.scene);

  // 禁用 expressionManager 自动 update（防止模型自带动画/初始值驱动 morph target）
  const em = exprMgr();
  if (em && typeof em.update === 'function') em.update = () => {};

  // 清零所有 morph target
  clearAllExpr();

  // ---- 模型信息 ----
  const meta = vrm.meta || {};
  const e = exprMgr();
  const exprNames = (e?._expressionMap) ? Object.keys(e._expressionMap) : [];

  const lines = [];
  lines.push('Model: ' + esc(meta.title || meta.name || 'N/A'));
  lines.push('Author: ' + esc(meta.author || 'N/A'));
  lines.push('Expressions: ' + exprNames.join(', '));
  infoEl.innerHTML = lines.join('<br>');
  console.log('=== VRM Model Info ===');
  lines.forEach(l => console.log(l.replace(/<[^>]*>/g, '')));

  // 隐藏加载状态
  statusEl.style.opacity = '0';
  setTimeout(() => { statusEl.classList.add('hidden'); }, 600);

  // 通知 C# 端
  if (window.chrome?.webview) {
    window.chrome.webview.postMessage({
      type: 'ready',
      model: meta.title || meta.name || 'unknown',
      expressions: exprNames,
    });
  }

  // ============================================================
  // 动画循环（只渲染 + 物理，不驱动表情）
  // ============================================================
  const clock = new THREE.Clock();
  function animate() {
    requestAnimationFrame(animate);
    const dt = Math.min(clock.getDelta(), 0.1);
    controls.update();
    if (vrm) vrm.update(dt);
    renderer.render(scene, camera);
  }
  animate();

} catch (err) {
  console.error(err);
  showError('<div>Failed</div><div style="font-size:12px;margin-top:8px">' + esc(err.message || String(err)) + '</div>');
}

// ---- 窗口 resize ----
window.addEventListener('resize', () => {
  camera.aspect = window.innerWidth / window.innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(window.innerWidth, window.innerHeight);
});
