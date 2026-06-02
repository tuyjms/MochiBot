// ============================================================
// VRM Viewer 配置常量
// 所有可调参数集中管理，修改此文件即可调整渲染/动画/交互行为
// ============================================================

// ---- 相机 ----
export const CAMERA = {
  FOV: 28,
  NEAR: 0.1,
  FAR: 50,
  POSITION: [0, 1.25, 3.0],
  TARGET: [0, 1.05, 0],
  MIN_DISTANCE: 1.0,
  MAX_DISTANCE: 5,
  MAX_POLAR_ANGLE: Math.PI * 0.6,
  DAMPING_FACTOR: 0.08,
  ENABLE_PAN: false,
};

// ---- 灯光 ----
export const LIGHTS = {
  AMBIENT: { color: '#ffffff', intensity: 1.4 },
  DIR_1: { color: '#ffffff', intensity: 1.8, position: [0.5, 2, 2] },
  DIR_2: { color: '#ddeeff', intensity: 0.6, position: [-1, 1, -0.5] },
};

// ---- 渲染器 ----
export const RENDERER = {
  MAX_PIXEL_RATIO: 2,
  TONE_MAPPING_EXPOSURE: 1.0,
  CLEAR_COLOR: 0x000000,
  CLEAR_ALPHA: 0,
};

// ---- Idle 骨骼动画 ----
// 每个参数为 [振幅, 频率, 相位偏移]，对应 amp * sin(t * freq + phase)
export const IDLE = {
  HIPS:       { rotZ: [0.015, 0.7, 0],         rotX: [0.01,  1.1, 1],       rotY: [0.02, 0.5, 2] },
  SPINE:      { rotZ: [0.012, 0.7, Math.PI],   rotX: [0.008, 1.3, 2] },
  CHEST:      { rotX: [0.01,  1.4, 1] },
  HEAD:       { rotX: [0.025, 0.6, 1.5],       rotY: [0.035, 0.4, 3],       rotZ: [0.02, 0.55, 0] },
  UPPER_ARM:  { rotZ: [0.025, 0.5] },             // 右臂相位 +π
  LOWER_ARM:  { rotZ: [0.015, 0.5, 0.3] },        // 右臂相位 +π
  HAND:       { rotX: [0.02,  0.8, 1],            rotZ: [0.015, 0.6, 2] },    // 右手相位 +π
  FINGERS:    { rotX: [0.03,  0.45] },             // 每指相位偏移 +0.7
  EYE:        { rotY: [0.02,  1.2, 0],            rotX: [0.015, 0.9, 1] },
  JAW:        { rotX: [0.005, 2.0, 3] },
};

// ---- 眨眼 ----
export const BLINK = {
  CLOSE_MS: 50,
  HOLD_MS: 50,
  OPEN_MS: 50,
  MIN_INTERVAL_MS: 2000,
  MAX_INTERVAL_MS: 7000,
};

// ---- 呼吸 ----
export const BREATH = {
  SCALE_Y: 0.008,
  SCALE_X: 0.004,
  SPEED: 1.5,
};

// ---- 情绪 ----
export const MOOD = {
  CYCLE_SEC: 5,
  EXPRESSION_INTENSITY: 0.8,
};

// ---- 视线追踪 ----
export const LOOK = {
  CYCLE_SEC: 8,
  HORIZONTAL: 0.4,
  VERTICAL: 0.3,
};

// ---- 表情映射 ----
export const MOODS = ['neutral', 'happy', 'angry', 'sad', 'relaxed'];
export const MOOD_MAP = { happy: 'happy', sad: 'sad', angry: 'angry', neutral: 'neutral' };
