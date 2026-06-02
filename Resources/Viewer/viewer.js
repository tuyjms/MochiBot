// ============================================================
// VRM Viewer — 3D 桌宠渲染模块
// 通过 PostWebMessageAsJson 接收 C# 事件，驱动表情和动作
// ============================================================

import {
  CAMERA, LIGHTS, RENDERER, IDLE, BLINK, BREATH,
  MOOD, LOOK, MOODS, MOOD_MAP,
} from './viewer-config.js';

import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { VRMLoaderPlugin, VRMUtils } from 'https://cdn.jsdelivr.net/npm/@pixiv/three-vrm@2.1.3/lib/three-vrm.module.min.js';

// ---- DOM ----
const statusEl = document.getElementById('status');
const infoEl   = document.getElementById('info');
const moodEl   = document.getElementById('mood');

// ---- 错误处理 ----
function showError(html) {
  statusEl.className = 'error';
  statusEl.style.opacity = '1';
  statusEl.innerHTML = html;
}

function esc(s) { return String(s || '').replace(/</g, '&lt;'); }

// ---- URL 参数 ----
const params     = new URLSearchParams(location.search);
const modelPath  = params.get('model');
const motionPath = params.get('motion');

if (!modelPath) {
  showError('<div>Missing ?model= parameter</div>');
  throw new Error('no model');
}

const base   = params.get('base') || 'https://vrm.local/';
const vrmUrl = base + modelPath.split('/').map(s => encodeURIComponent(s)).join('/');

statusEl.innerHTML = '<div class="spinner"></div><div>Loading...</div>';

// ============================================================
// Three.js 场景
// ============================================================

const renderer = new THREE.WebGLRenderer({
  antialias: true,
  alpha: true,
  premultipliedAlpha: false,
});
renderer.setPixelRatio(Math.min(window.devicePixelRatio, RENDERER.MAX_PIXEL_RATIO));
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = RENDERER.TONE_MAPPING_EXPOSURE;
renderer.setClearColor(RENDERER.CLEAR_COLOR, RENDERER.CLEAR_ALPHA);
document.body.appendChild(renderer.domElement);

const scene = new THREE.Scene();
scene.background = null;

const camera = new THREE.PerspectiveCamera(
  CAMERA.FOV, window.innerWidth / window.innerHeight, CAMERA.NEAR, CAMERA.FAR
);
camera.position.set(...CAMERA.POSITION);

const controls = new OrbitControls(camera, renderer.domElement);
controls.target.set(...CAMERA.TARGET);
controls.enableDamping = true;
controls.dampingFactor = CAMERA.DAMPING_FACTOR;
controls.minDistance = CAMERA.MIN_DISTANCE;
controls.maxDistance = CAMERA.MAX_DISTANCE;
controls.maxPolarAngle = CAMERA.MAX_POLAR_ANGLE;
controls.enablePan = CAMERA.ENABLE_PAN;
controls.update();

// 灯光
scene.add(new THREE.AmbientLight(LIGHTS.AMBIENT.color, LIGHTS.AMBIENT.intensity));
const dir1 = new THREE.DirectionalLight(LIGHTS.DIR_1.color, LIGHTS.DIR_1.intensity);
dir1.position.set(...LIGHTS.DIR_1.position);
scene.add(dir1);
const dir2 = new THREE.DirectionalLight(LIGHTS.DIR_2.color, LIGHTS.DIR_2.intensity);
dir2.position.set(...LIGHTS.DIR_2.position);
scene.add(dir2);

// ============================================================
// 状态
// ============================================================

let vrm = null, mixer = null, motionMixer = null, motionAction = null;
let autoIdleBone = true, autoBlink = true, autoMood = true, autoLook = true, autoBreath = false;
let motionPlaying = false, motionLoop = true;
let idleTime = 0, moodTime = 0, lookTime = 0, breathTime = 0;
let blinkTimer = null;
let currentMood = 'neutral';

// ============================================================
// UI 事件绑定
// ============================================================

['btnIdle', 'btnBlink', 'btnMood', 'btnLook', 'btnBreath'].forEach(id => {
  document.getElementById(id).addEventListener('click', function () {
    const key = id.replace('btn', '').toLowerCase();
    const map = { idle: 'autoIdleBone', blink: 'autoBlink', mood: 'autoMood', look: 'autoLook', breath: 'autoBreath' };
    window[map[key]] = !window[map[key]];
    this.classList.toggle('on', window[map[key]]);
    if (key === 'blink' && !autoBlink && blinkTimer) clearTimeout(blinkTimer);
    if (key === 'mood' && !autoMood) {
      setExpr('neutral', 1); setExpr(currentMood, 0); currentMood = 'neutral'; moodEl.style.opacity = '0';
    }
    if (key === 'breath' && !autoBreath) breathTime = 0;
  });
});

document.getElementById('btnReset').addEventListener('click', () => {
  camera.position.set(...CAMERA.POSITION);
  controls.target.set(...CAMERA.TARGET);
  controls.update();
});

document.getElementById('btnMotion').addEventListener('click', function () {
  if (!motionAction) return;
  motionPlaying = !motionPlaying;
  this.classList.toggle('on', motionPlaying);
  if (motionPlaying) { motionAction.paused = false; motionAction.play(); }
  else { motionAction.paused = true; }
});

document.getElementById('btnMotionLoop').addEventListener('click', function () {
  if (!motionAction) return;
  motionLoop = !motionLoop;
  this.classList.toggle('on', motionLoop);
  motionAction.loop = motionLoop ? THREE.LoopRepeat : THREE.LoopOnce;
  if (!motionLoop) motionAction.clampWhenFinished = true;
});

// ============================================================
// 通用工具
// ============================================================

/** 计算 amp * sin(t * freq + phase) */
function wave(t, p) { return p[0] * Math.sin(t * p[1] + (p[2] || 0)); }

const exprMgr = () => vrm && (vrm.expressionManager || vrm.blendShapeProxy);

function setExpr(name, val) {
  if (!vrm) return;
  const e = exprMgr();
  if (!e) return;
  try {
    if (typeof e.setExpressionValue === 'function') e.setExpressionValue(name, val);
    else if (typeof e.setValue === 'function') e.setValue(name, val);
  } catch (_) { /* 表情不存在时静默忽略 */ }
}

// ---- 眨眼 ----
function scheduleBlink() {
  if (!autoBlink || !vrm) return;
  const interval = BLINK.MIN_INTERVAL_MS + Math.random() * (BLINK.MAX_INTERVAL_MS - BLINK.MIN_INTERVAL_MS);
  blinkTimer = setTimeout(() => {
    const t0 = performance.now();
    const total = BLINK.CLOSE_MS + BLINK.HOLD_MS + BLINK.OPEN_MS;
    function step() {
      const e = performance.now() - t0;
      let v;
      if (e < BLINK.CLOSE_MS) v = e / BLINK.CLOSE_MS;
      else if (e < BLINK.CLOSE_MS + BLINK.HOLD_MS) v = 1;
      else if (e < total) v = 1 - (e - BLINK.CLOSE_MS - BLINK.HOLD_MS) / BLINK.OPEN_MS;
      else v = 0;
      setExpr('blink', v);
      if (e < total) requestAnimationFrame(step);
      else { setExpr('blink', 0); scheduleBlink(); }
    }
    step();
  }, interval);
}

// ============================================================
// C# → JS 消息通道 (PostWebMessageAsJson)
// ============================================================

function handleMessage(msg) {
  if (!msg || !msg.type) return;
  switch (msg.type) {
    case 'mood':        handleMoodMessage(msg.expression); break;
    case 'motion':      handleMotionMessage(msg.motionName); break;
    case 'motion_stop': handleMotionStop(); break;
    case 'toggle':      handleToggle(msg.feature, msg.enabled); break;
  }
}

function handleMoodMessage(expression) {
  if (!vrm) return;
  const expr = MOOD_MAP[(expression || '').toLowerCase()];
  if (!expr) return;

  autoMood = false;
  document.getElementById('btnMood').classList.remove('on');

  setExpr(currentMood, 0);
  currentMood = expr;
  setExpr(expr, MOOD.EXPRESSION_INTENSITY);
  moodEl.textContent = expr;
  moodEl.style.opacity = '1';
  setTimeout(() => { moodEl.style.opacity = '0'; }, 2000);

  if (expr === 'neutral') {
    autoMood = true;
    document.getElementById('btnMood').classList.add('on');
  }
}

async function handleMotionMessage(motionName) {
  if (!vrm || !motionName) return;
  const motionUrl = 'https://vrm.local/Data/' + encodeURIComponent(motionName) + '.vrma';

  try {
    if (motionAction) { motionAction.stop(); motionAction = null; }
    if (motionMixer) { motionMixer.stopAllAction(); motionMixer = null; }

    const { VRMAnimationLoaderPlugin, createVRMAnimationClip } = await import(
      'https://cdn.jsdelivr.net/npm/@pixiv/three-vrm-animation@3.3.4/lib/three-vrm-animation.module.js'
    );
    const motionLoader = new GLTFLoader();
    motionLoader.register(parser => new VRMAnimationLoaderPlugin(parser));
    const motionGltf = await motionLoader.loadAsync(motionUrl);
    const vrmAnimations = motionGltf.userData.vrmAnimations;
    if (vrmAnimations && vrmAnimations.length > 0) {
      const clip = createVRMAnimationClip(vrmAnimations[0], vrm);
      motionMixer = new THREE.AnimationMixer(vrm.scene);
      motionAction = motionMixer.clipAction(clip);
      motionAction.loop = motionLoop ? THREE.LoopRepeat : THREE.LoopOnce;
      motionAction.play();
      motionPlaying = true;
      document.getElementById('btnMotion').classList.add('on');
      console.log('Motion loaded:', motionName, 'duration:', clip.duration);
    }
  } catch (err) {
    console.error('Failed to load motion:', motionName, err);
  }
}

function handleMotionStop() {
  if (motionAction) { motionAction.stop(); motionAction = null; }
  if (motionMixer) { motionMixer.stopAllAction(); motionMixer = null; }
  motionPlaying = false;
  document.getElementById('btnMotion').classList.remove('on');
}

function handleToggle(feature, enabled) {
  const map = { idle: 'autoIdleBone', blink: 'autoBlink', mood: 'autoMood', look: 'autoLook', breath: 'autoBreath' };
  const key = map[feature];
  if (!key) return;
  window[key] = !!enabled;
  const btnId = 'btn' + feature.charAt(0).toUpperCase() + feature.slice(1);
  const btn = document.getElementById(btnId);
  if (btn) btn.classList.toggle('on', !!enabled);
  if (feature === 'blink' && !autoBlink && blinkTimer) clearTimeout(blinkTimer);
  if (feature === 'mood' && !autoMood) {
    setExpr('neutral', 1); setExpr(currentMood, 0); currentMood = 'neutral'; moodEl.style.opacity = '0';
  }
}

if (window.chrome?.webview) {
  window.chrome.webview.addEventListener('message', e => handleMessage(e.data));
}

// ============================================================
// 骨骼查找 (兼容 three-vrm v2 内部结构)
// ============================================================

function getBone(boneName) {
  if (!vrm || !vrm.humanoid) return null;
  const hum = vrm.humanoid;
  try {
    if (typeof hum.getNormalizedBoneNode === 'function') {
      const node = hum.getNormalizedBoneNode(boneName);
      if (node) return node;
    }
  } catch (_) {}
  try {
    const hb = (hum._normalizedHumanBones || {}).humanBones;
    if (hb && hb[boneName]) return hb[boneName];
  } catch (_) {}
  try {
    const hb = (hum._rawHumanBones || {}).humanBones;
    if (hb && hb[boneName]) return hb[boneName];
  } catch (_) {}
  return null;
}

// ============================================================
// Idle 骨骼动画（参数来自 IDLE 常量）
// ============================================================

const FINGER_BONES = [
  'leftThumbProximal', 'leftIndexProximal', 'leftMiddleProximal', 'leftRingProximal', 'leftLittleProximal',
  'rightThumbProximal', 'rightIndexProximal', 'rightMiddleProximal', 'rightRingProximal', 'rightLittleProximal',
];

function applyBoneIdle(t) {
  const PI = Math.PI;

  // ---- 躯干 ----
  const hips = getBone('hips');
  if (hips) {
    hips.rotation.z = wave(t, IDLE.HIPS.rotZ);
    hips.rotation.x = wave(t, IDLE.HIPS.rotX);
    hips.rotation.y = wave(t, IDLE.HIPS.rotY);
  }
  const spine = getBone('spine');
  if (spine) {
    spine.rotation.z = wave(t, IDLE.SPINE.rotZ);
    spine.rotation.x = wave(t, IDLE.SPINE.rotX);
  }
  const chest = getBone('chest');
  if (chest) { chest.rotation.x = wave(t, IDLE.CHEST.rotX); }

  // ---- 头部 ----
  const head = getBone('head');
  if (head) {
    head.rotation.x = wave(t, IDLE.HEAD.rotX);
    head.rotation.y = wave(t, IDLE.HEAD.rotY);
    head.rotation.z = wave(t, IDLE.HEAD.rotZ);
  }

  // ---- 手臂（右臂相位 +π）----
  const lUpperArm = getBone('leftUpperArm');
  if (lUpperArm) { lUpperArm.rotation.z = wave(t, IDLE.UPPER_ARM.rotZ); }
  const rUpperArm = getBone('rightUpperArm');
  if (rUpperArm) { rUpperArm.rotation.z = wave(t, [IDLE.UPPER_ARM.rotZ[0], IDLE.UPPER_ARM.rotZ[1], (IDLE.UPPER_ARM.rotZ[2] || 0) + PI]); }
  const lLowerArm = getBone('leftLowerArm');
  if (lLowerArm) { lLowerArm.rotation.z = wave(t, IDLE.LOWER_ARM.rotZ); }
  const rLowerArm = getBone('rightLowerArm');
  if (rLowerArm) { rLowerArm.rotation.z = wave(t, [IDLE.LOWER_ARM.rotZ[0], IDLE.LOWER_ARM.rotZ[1], (IDLE.LOWER_ARM.rotZ[2] || 0) + PI]); }

  // ---- 手（右手相位 +π）----
  const lHand = getBone('leftHand');
  if (lHand) {
    lHand.rotation.x = wave(t, IDLE.HAND.rotX);
    lHand.rotation.z = wave(t, IDLE.HAND.rotZ);
  }
  const rHand = getBone('rightHand');
  if (rHand) {
    rHand.rotation.x = wave(t, [IDLE.HAND.rotX[0], IDLE.HAND.rotX[1], (IDLE.HAND.rotX[2] || 0) + PI]);
    rHand.rotation.z = wave(t, [IDLE.HAND.rotZ[0], IDLE.HAND.rotZ[1], (IDLE.HAND.rotZ[2] || 0) + PI]);
  }

  // ---- 手指 ----
  FINGER_BONES.forEach((name, i) => {
    const b = getBone(name);
    if (b) { b.rotation.x = wave(t, [IDLE.FINGERS.rotX[0], IDLE.FINGERS.rotX[1], i * 0.7]); }
  });

  // ---- 眼睛 ----
  const lEye = getBone('leftEye');
  if (lEye) { lEye.rotation.y = wave(t, IDLE.EYE.rotY); lEye.rotation.x = wave(t, IDLE.EYE.rotX); }
  const rEye = getBone('rightEye');
  if (rEye) { rEye.rotation.y = wave(t, IDLE.EYE.rotY); rEye.rotation.x = wave(t, IDLE.EYE.rotX); }

  // ---- 下巴 ----
  const jaw = getBone('jaw');
  if (jaw) { jaw.rotation.x = wave(t, IDLE.JAW.rotX); }
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

  // ---- 模型信息 ----
  const meta = vrm.meta || {};
  const sb = vrm.springBoneManager;
  let sbDebug = '';
  if (sb) {
    for (let k of Object.keys(sb)) {
      const v = sb[k];
      if (Array.isArray(v)) sbDebug += k + '=' + v.length + ' ';
      else if (v instanceof Map) sbDebug += k + '=Map(' + v.size + ') ';
      else sbDebug += k + '=' + typeof v + ' ';
    }
  }
  let boneNames = [];
  let boneDebug = '';
  if (vrm.humanoid) {
    const hum = vrm.humanoid;
    const n = hum._normalizedHumanBones;
    if (n && n.humanBones) {
      const keys = Object.keys(n.humanBones);
      boneDebug += '_normalizedHumanBones={' + keys.slice(0, 10).join(',') + '...}(n=' + keys.length + ') ';
      boneNames = keys;
    }
    const r = hum._rawHumanBones;
    if (r && r.humanBones && boneNames.length === 0) {
      const keys = Object.keys(r.humanBones);
      boneDebug += '_rawHumanBones={' + keys.slice(0, 10).join(',') + '...}(n=' + keys.length + ') ';
      boneNames = keys;
    }
    if (boneNames.length === 0 && typeof hum.getNormalizedBoneNode === 'function') {
      const stdBones = [
        'hips', 'spine', 'chest', 'upperChest', 'neck', 'head',
        'leftUpperArm', 'leftLowerArm', 'leftHand',
        'rightUpperArm', 'rightLowerArm', 'rightHand',
        'leftUpperLeg', 'leftLowerLeg', 'leftFoot',
        'rightUpperLeg', 'rightLowerLeg', 'rightFoot',
      ];
      for (let b of stdBones) {
        try { if (hum.getNormalizedBoneNode(b)) boneNames.push(b); } catch (_) {}
      }
    }
  }
  const exprNames = [];
  const e = exprMgr();
  if (e && e._expressionMap) {
    for (let k of Object.keys(e._expressionMap)) exprNames.push(k);
  }

  const lines = [];
  lines.push('Model: ' + esc(meta.title || meta.name || 'N/A'));
  lines.push('Author: ' + esc(meta.author || 'N/A'));
  lines.push('Humanoid bones: ' + (boneNames.length > 0 ? boneNames.join(', ') : 'none detected'));
  lines.push('BoneDebug: ' + boneDebug);
  lines.push('Expressions: ' + exprNames.join(', '));
  lines.push('SpringBones: ' + sbDebug);
  lines.push('AnimClips: ' + (gltf.animations?.length || 0));
  infoEl.innerHTML = lines.join('<br>');
  console.log('=== VRM Model Info ===');
  lines.forEach(l => console.log(l.replace(/<[^>]*>/g, '')));

  // 内置动画
  if (gltf.animations && gltf.animations.length > 0) {
    mixer = new THREE.AnimationMixer(vrm.scene);
    gltf.animations.forEach(c => { const a = mixer.clipAction(c); a.play(); });
  }

  // 隐藏加载状态
  statusEl.style.opacity = '0';
  setTimeout(() => { statusEl.classList.add('hidden'); }, 600);
  scheduleBlink();

  // ---- URL 参数加载动作 ----
  if (motionPath) {
    const motionName = motionPath.split('/').pop().replace(/\.vrma$/i, '');
    await handleMotionMessage(motionName);
  }

  // ---- 通知 C# 端 ----
  if (window.chrome?.webview) {
    window.chrome.webview.postMessage({
      type: 'ready',
      model: meta.title || meta.name || 'unknown',
      expressions: exprNames,
      bones: boneNames.length,
    });
  }

  // ============================================================
  // 动画循环
  // ============================================================
  const clock = new THREE.Clock();
  function animate() {
    requestAnimationFrame(animate);
    const dt = Math.min(clock.getDelta(), 0.1);
    controls.update();

    if (vrm) {
      vrm.update(dt);
      if (motionMixer) motionMixer.update(dt);
      if (mixer) mixer.update(dt);
    }

    // Idle 骨骼（动作播放时暂停）
    if (autoIdleBone && !motionPlaying) {
      idleTime += dt;
      applyBoneIdle(idleTime);
    }

    // 呼吸
    if (autoBreath) {
      breathTime += dt;
      const chest = getBone('chest') || getBone('upperChest') || getBone('spine');
      if (chest) {
        chest.scale.y = 1 + BREATH.SCALE_Y * Math.sin(breathTime * BREATH.SPEED);
        chest.scale.x = 1 + BREATH.SCALE_X * Math.sin(breathTime * BREATH.SPEED);
      }
    }

    // 情绪循环
    if (autoMood) {
      moodTime += dt;
      const idx = Math.floor(moodTime / MOOD.CYCLE_SEC) % MOODS.length;
      const newMood = MOODS[idx];
      if (newMood !== currentMood) {
        setExpr(currentMood, 0);
        currentMood = newMood;
        setExpr(currentMood, MOOD.EXPRESSION_INTENSITY);
        moodEl.textContent = currentMood;
        moodEl.style.opacity = '1';
        setTimeout(() => { if (autoMood) moodEl.style.opacity = '0'; }, 2000);
      }
    }

    // 视线追踪
    if (autoLook) {
      lookTime += dt;
      const c = lookTime % LOOK.CYCLE_SEC;
      setExpr('lookLeft',  c < 2 ? LOOK.HORIZONTAL : 0);
      setExpr('lookRight', c >= 3 && c < 5 ? LOOK.HORIZONTAL : 0);
      setExpr('lookUp',    c >= 6 && c < 6.5 ? LOOK.VERTICAL : 0);
      setExpr('lookDown',  c >= 7 ? LOOK.VERTICAL : 0);
    }

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
