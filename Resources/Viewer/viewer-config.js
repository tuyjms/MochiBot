// ============================================================
// VRM Viewer 配置常量（最简版）
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

// ---- 表情映射 ----
export const MOOD_MAP = {
  happy: 'happy', sad: 'sad', angry: 'angry', neutral: 'neutral',
  relaxed: 'relaxed', surprised: 'surprised',
  sleepy: 'relaxed', touched: 'happy', teasing: 'happy',
};

// ---- Agent 状态 → VRM 效果 ----
export const STATE_EFFECTS = {
  thinking:  { expression: 'relaxed', lookX: 0, lookY: -0.3 },
  error:     { expression: 'surprised', lookX: 0, lookY: 0 },
  reloading: { reset: true },
  idle:      { reset: true },
};
