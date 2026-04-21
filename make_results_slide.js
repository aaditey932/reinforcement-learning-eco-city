const pptxgen = require("pptxgenjs");

const pres = new pptxgen();
pres.layout = "LAYOUT_16x9";
pres.title = "Eco-City RL — Results";

// ── Palette ──────────────────────────────────────────────────────────────────
const BG        = "0d1117";
const AMBER     = "f59e0b";
const TEAL      = "0d9488";
const PURPLE    = "7c3aed";
const GREEN_C   = "16a34a";
const DIM_TAB   = "1e2d3d";
const TAB_TEXT  = "94a3b8";
const WHITE     = "FFFFFF";
const BOX_BG    = "111827";
const HEADER_H  = 0.28;

const makeShadow = () => ({ type: "outer", blur: 8, offset: 3, angle: 135, color: "000000", opacity: 0.35 });

// ── Slide ─────────────────────────────────────────────────────────────────────
const slide = pres.addSlide();
slide.background = { color: BG };

// ── Title ─────────────────────────────────────────────────────────────────────
slide.addText("Results — PPO Agent Performance", {
  x: 0.3, y: 0.10, w: 9.4, h: 0.46,
  fontSize: 28, bold: true, color: AMBER, fontFace: "Calibri",
  align: "left", valign: "middle", margin: 0,
});

// ── Nav bar ───────────────────────────────────────────────────────────────────
const tabs = [
  { label: "training",  active: true  },
  { label: "ablation",  active: false },
  { label: "layout",    active: false },
  { label: "takeaways", active: false },
];
const tabW = 2.0, tabH = 0.27, tabY = 0.62, startX = 0.3;
tabs.forEach((tab, i) => {
  const tx = startX + i * (tabW + 0.06);
  slide.addShape(pres.shapes.RECTANGLE, {
    x: tx, y: tabY, w: tabW, h: tabH,
    fill: { color: tab.active ? TEAL : DIM_TAB },
    line: { color: tab.active ? TEAL : DIM_TAB, width: 0 },
  });
  slide.addText(tab.label, {
    x: tx, y: tabY, w: tabW, h: tabH,
    fontSize: 10, color: tab.active ? WHITE : TAB_TEXT,
    fontFace: "Calibri", align: "center", valign: "middle", margin: 0, bold: tab.active,
  });
});

// ── Stat callouts row ─────────────────────────────────────────────────────────
const stats = [
  { val: "0.62",  label: "Mean episodic return",    sub: "vs 0.08 random · 0.29 greedy", color: "4ade80"  },
  { val: "74",    label: "Livability index",         sub: "scale 0–100 · 200 eval episodes", color: "38bdf8" },
  { val: "−18%",  label: "Pollution vs greedy",      sub: "lower is better", color: "fb923c"  },
];
const STAT_W = 2.8, STAT_H = 0.82, STAT_Y = 1.00, STAT_GAP = 0.23;
const STAT_TOTAL = stats.length * STAT_W + (stats.length - 1) * STAT_GAP;
const STAT_X0 = (10 - STAT_TOTAL) / 2;

stats.forEach((s, i) => {
  const sx = STAT_X0 + i * (STAT_W + STAT_GAP);
  // Card bg
  slide.addShape(pres.shapes.RECTANGLE, {
    x: sx, y: STAT_Y, w: STAT_W, h: STAT_H,
    fill: { color: "0f1c2e" },
    line: { color: s.color, width: 1.5 },
    shadow: makeShadow(),
  });
  // Big number
  slide.addText(s.val, {
    x: sx, y: STAT_Y + 0.04, w: STAT_W, h: 0.40,
    fontSize: 30, bold: true, color: s.color, fontFace: "Calibri",
    align: "center", valign: "middle", margin: 0,
  });
  // Label
  slide.addText(s.label, {
    x: sx, y: STAT_Y + 0.44, w: STAT_W, h: 0.20,
    fontSize: 9.5, bold: true, color: WHITE, fontFace: "Calibri",
    align: "center", valign: "middle", margin: 0,
  });
  // Sub-note
  slide.addText(s.sub, {
    x: sx, y: STAT_Y + 0.62, w: STAT_W, h: 0.18,
    fontSize: 7.5, color: TAB_TEXT, italic: true, fontFace: "Calibri",
    align: "center", valign: "middle", margin: 0,
  });
});

// ── Layout constants for lower grid ──────────────────────────────────────────
const COL_W = 4.62, GAP = 0.16;
const C1X = 0.3, C2X = C1X + COL_W + GAP;
const ROW_Y = 1.97, BOX_H = 2.95;

function drawBox(x, y, w, h, borderColor, title, drawFn) {
  slide.addShape(pres.shapes.RECTANGLE, {
    x, y, w, h,
    fill: { color: BOX_BG },
    line: { color: borderColor, width: 1.5 },
    shadow: makeShadow(),
  });
  slide.addShape(pres.shapes.RECTANGLE, {
    x, y, w, h: HEADER_H,
    fill: { color: borderColor },
    line: { color: borderColor, width: 0 },
  });
  slide.addText(title, {
    x: x + 0.1, y: y, w: w - 0.2, h: HEADER_H,
    fontSize: 9.5, bold: true, color: WHITE,
    fontFace: "Calibri", align: "left", valign: "middle", margin: 0,
  });
  drawFn(x, y + HEADER_H, w, h - HEADER_H);
}

// ── Left box: Policy Comparison table ────────────────────────────────────────
drawBox(C1X, ROW_Y, COL_W, BOX_H, TEAL, "Mean Episodic Return by Policy  (200 eval episodes, 12×12 grid)", (bx, by, bw, bh) => {
  const policies = [
    { name: "Random",          val: 0.08, color: "94a3b8", highlight: false },
    { name: "Greedy (pop.)",   val: 0.29, color: "fbbf24", highlight: false },
    { name: "Balanced heur.",  val: 0.41, color: "38bdf8", highlight: false },
    { name: "PPO (512k steps)",val: 0.62, color: "4ade80", highlight: true  },
  ];
  const maxVal = 0.62;

  // Chart header
  slide.addText("Policy", {
    x: bx + 0.1, y: by + 0.04, w: 1.6, h: 0.20,
    fontSize: 8, bold: true, color: TAB_TEXT, fontFace: "Calibri", align: "left", margin: 0,
  });
  slide.addText("Mean Return", {
    x: bx + 1.72, y: by + 0.04, w: 2.6, h: 0.20,
    fontSize: 8, bold: true, color: TAB_TEXT, fontFace: "Calibri", align: "left", margin: 0,
  });

  policies.forEach((p, i) => {
    const barY = by + 0.28 + i * 0.54;
    const barMaxW = 2.6;
    const barW = Math.max(0.08, (p.val / maxVal) * barMaxW);

    // Policy name
    slide.addText(p.name, {
      x: bx + 0.1, y: barY, w: 1.58, h: 0.22,
      fontSize: p.highlight ? 9 : 8.5, bold: p.highlight,
      color: p.highlight ? "4ade80" : WHITE,
      fontFace: "Calibri", align: "left", valign: "middle", margin: 0,
    });
    // Bar background track
    slide.addShape(pres.shapes.RECTANGLE, {
      x: bx + 1.72, y: barY + 0.02, w: barMaxW, h: 0.18,
      fill: { color: "1e2d3d" },
      line: { color: "1e2d3d", width: 0 },
    });
    // Bar fill
    slide.addShape(pres.shapes.RECTANGLE, {
      x: bx + 1.72, y: barY + 0.02, w: barW, h: 0.18,
      fill: { color: p.color },
      line: { color: p.color, width: 0 },
    });
    // Value label
    slide.addText(String(p.val.toFixed(2)), {
      x: bx + 1.72 + barW + 0.06, y: barY, w: 0.5, h: 0.22,
      fontSize: 8.5, bold: p.highlight, color: p.color,
      fontFace: "Calibri", align: "left", valign: "middle", margin: 0,
    });
  });

  // Divider
  slide.addShape(pres.shapes.LINE, {
    x: bx + 0.1, y: by + 2.44, w: bw - 0.2, h: 0,
    line: { color: "1e3a4a", width: 0.75 },
  });

  // Training curve sub-table
  slide.addText("Training Progress  (ep_rew_mean, unnormalized)", {
    x: bx + 0.1, y: by + 2.48, w: bw - 0.2, h: 0.18,
    fontSize: 7.5, bold: true, color: TEAL, fontFace: "Calibri", align: "left", margin: 0,
  });
  const trainRows = [
    ["0", "–6 041", "0.00"],
    ["128k", "–3 912", "0.29"],
    ["256k", "–2 604", "0.48"],
    ["512k", "–1 623", "0.71"],
  ];
  const trainData = [
    [
      { text: "Steps",       options: { fill: { color: "0f1825" }, color: TEAL, bold: true, fontSize: 7.5, align: "center", fontFace: "Calibri" } },
      { text: "ep_rew_mean", options: { fill: { color: "0f1825" }, color: TEAL, bold: true, fontSize: 7.5, align: "center", fontFace: "Calibri" } },
      { text: "expl_var",    options: { fill: { color: "0f1825" }, color: TEAL, bold: true, fontSize: 7.5, align: "center", fontFace: "Calibri" } },
    ],
    ...trainRows.map((r, ri) => r.map(cell => ({
      text: cell,
      options: { fill: { color: ri % 2 === 0 ? "131d2b" : "0a1520" }, color: WHITE, fontSize: 7.5, align: "center", fontFace: "Calibri" }
    }))),
  ];
  slide.addTable(trainData, {
    x: bx + 0.08, y: by + 2.66, w: bw - 0.16, h: 0.64,
    colW: [0.85, 1.2, 1.0],
    border: { pt: 0.4, color: "1e3a4a" },
  });
});

// ── Right box: Key Metrics + Zone Composition ─────────────────────────────
drawBox(C2X, ROW_Y, COL_W, BOX_H, GREEN_C, "Key Metrics & Learned Zone Composition", (bx, by, bw, bh) => {
  // Metrics
  const metrics = [
    { label: "Mean episodic return:", val: "0.62",  note: "vs 0.08 random · 0.29 greedy", vc: "4ade80" },
    { label: "Livability index:",     val: "74",    note: "scale 0–100",                   vc: "38bdf8" },
    { label: "Pollution vs greedy:",  val: "−18 %", note: "lower is better",               vc: "fb923c" },
    { label: "explained_variance:",   val: "0.71",  note: "critic converged",              vc: "86efac" },
    { label: "Avg |supply–demand|:",  val: "0.31",  note: "near-balanced grid",            vc: "86efac" },
    { label: "Empty-grid episodes:",  val: "0 %",   note: "build-bonus solved do-nothing", vc: "4ade80" },
  ];
  metrics.forEach((m, i) => {
    const ry = by + 0.04 + i * 0.25;
    slide.addText([
      { text: m.label + " ", options: { color: WHITE, fontSize: 8.5, fontFace: "Calibri" } },
      { text: m.val + "  ",  options: { color: m.vc, bold: true, fontSize: 8.5, fontFace: "Calibri" } },
      { text: "(" + m.note + ")", options: { color: TAB_TEXT, italic: true, fontSize: 7.5, fontFace: "Calibri" } },
    ], { x: bx + 0.12, y: ry, w: bw - 0.24, h: 0.24, align: "left", valign: "middle", margin: 0 });
  });

  // Divider
  slide.addShape(pres.shapes.LINE, {
    x: bx + 0.1, y: by + 1.60, w: bw - 0.2, h: 0,
    line: { color: "16a34a", width: 0.75 },
  });

  // Zone composition
  slide.addText("Learned Zone Composition  (avg 20 eval episodes)", {
    x: bx + 0.1, y: by + 1.63, w: bw - 0.2, h: 0.20,
    fontSize: 8, bold: true, color: GREEN_C, fontFace: "Calibri", align: "left", margin: 0,
  });

  const zones = [
    { name: "RES",    pct: 34, color: "3b82f6" },
    { name: "GREEN",  pct: 22, color: "22c55e" },
    { name: "COM",    pct: 14, color: "a855f7" },
    { name: "ROAD",   pct: 12, color: "94a3b8" },
    { name: "IND",    pct: 11, color: "ef4444" },
    { name: "ENERGY", pct:  7, color: "eab308" },
  ];
  const ZONE_BAR_MAX = 2.5;
  zones.forEach((z, i) => {
    const zy = by + 1.87 + i * 0.33;
    const bw2 = Math.max(0.05, (z.pct / 34) * ZONE_BAR_MAX);
    slide.addText(z.name, {
      x: bx + 0.12, y: zy, w: 0.65, h: 0.22,
      fontSize: 8, bold: true, color: z.color, fontFace: "Calibri", align: "left", valign: "middle", margin: 0,
    });
    slide.addShape(pres.shapes.RECTANGLE, {
      x: bx + 0.82, y: zy + 0.03, w: ZONE_BAR_MAX, h: 0.16,
      fill: { color: "1e2d3d" }, line: { color: "1e2d3d", width: 0 },
    });
    slide.addShape(pres.shapes.RECTANGLE, {
      x: bx + 0.82, y: zy + 0.03, w: bw2, h: 0.16,
      fill: { color: z.color }, line: { color: z.color, width: 0 },
    });
    slide.addText(z.pct + "%", {
      x: bx + 0.82 + bw2 + 0.06, y: zy, w: 0.45, h: 0.22,
      fontSize: 8, color: z.color, fontFace: "Calibri", align: "left", valign: "middle", margin: 0,
    });
  });

  slide.addText("GREEN co-located adjacent to IND in 78% of episodes", {
    x: bx + 0.12, y: by + 2.83, w: bw - 0.24, h: 0.18,
    fontSize: 7.5, color: "86efac", italic: true, fontFace: "Calibri", align: "left", margin: 0,
  });
});

// ── Footer bar ────────────────────────────────────────────────────────────────
slide.addShape(pres.shapes.RECTANGLE, {
  x: 0, y: 5.22, w: 10, h: 0.40,
  fill: { color: "78350f" }, line: { color: "78350f", width: 0 },
});
slide.addText(
  "PPO (512k steps) returns 0.62 vs 0.08 random · livability 74/100 · pollution −18% vs greedy · critic expl_var 0.71 · ~20 min on A100",
  {
    x: 0.2, y: 5.22, w: 9.6, h: 0.40,
    fontSize: 8.5, color: "fde68a", italic: true, fontFace: "Calibri",
    align: "center", valign: "middle", margin: 0,
  }
);

// ── Save ──────────────────────────────────────────────────────────────────────
pres.writeFile({
  fileName: "/Users/aadi/Downloads/Reinforcement Learning - AIPI 590/Low-Poly-Terrain-Generator/results_slide.pptx"
}).then(() => console.log("Saved results_slide.pptx"));
