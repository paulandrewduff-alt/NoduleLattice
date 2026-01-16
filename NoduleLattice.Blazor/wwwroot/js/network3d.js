// ============================================================================
// FILE: NoduleLattice.Blazor/wwwroot/js/network3d.js
// PURPOSE (Visual notch #3):
//   - Replace billboard points with instanced SOMA SPHERES (no more giant quads).
//   - Strong region base colour + activity overlay (keeps cortex/thalamus/nuclei visible).
//   - Edge thinning: local-first, long-range de-emphasised, still performance-friendly.
//   - Correct hover picking for InstancedMesh (true node ids).
// ============================================================================

function requireThree() {
    if (!window.THREE) throw new Error("THREE is not loaded. Ensure App.razor includes three.min.js.");
    if (!window.THREE.OrbitControls) throw new Error("OrbitControls is not loaded. Ensure App.razor includes OrbitControls.js.");
    return window.THREE;
}

function clamp(x, lo, hi) { return x < lo ? lo : (x > hi ? hi : x); }
function lerp(a, b, t) { return a + (b - a) * t; }

function normalizeSnapshot(snapshot) {
    return {
        stepIndex: snapshot.stepIndex ?? snapshot.StepIndex ?? 0,
        nodes: snapshot.nodes ?? snapshot.Nodes ?? [],
        synapses: snapshot.synapses ?? snapshot.Synapses ?? []
    };
}

function normalizeView(view) {
    view = view || {};
    return {
        spacingXY: view.spacingXY ?? view.SpacingXY ?? 1.20,
        zScale: view.zScale ?? view.ZScale ?? 1.70,

        nodeSize: view.nodeSize ?? view.NodeSize ?? 0.10, // sphere radius-ish
        nodeOpacity: view.nodeOpacity ?? view.NodeOpacity ?? 0.92,

        edgeSoftFadeLen: view.edgeSoftFadeLen ?? view.EdgeSoftFadeLen ?? 4.2,
        edgeHardCullLen: view.edgeHardCullLen ?? view.EdgeHardCullLen ?? 6.2,
        edgeOpacity: view.edgeOpacity ?? view.EdgeOpacity ?? 0.28,

        showGrid: view.showGrid ?? view.ShowGrid ?? true,
        showAxes: view.showAxes ?? view.ShowAxes ?? false,
        showLaminarPlanes: view.showLaminarPlanes ?? view.ShowLaminarPlanes ?? true,

        enableFog: view.enableFog ?? view.EnableFog ?? true,
        fogDensity: view.fogDensity ?? view.FogDensity ?? 0.020,

        hoverPointThreshold: view.hoverPointThreshold ?? view.HoverPointThreshold ?? 0.55,
        columnBoost: view.columnBoost ?? view.ColumnBoost ?? 0.55,
        hoverBoost: view.hoverBoost ?? view.HoverBoost ?? 0.90,

        enableRegions: view.enableRegions ?? view.EnableRegions ?? true,
        thalamusRadiusXY: view.thalamusRadiusXY ?? view.ThalamusRadiusXY ?? 0.28,
        thalamusRadiusZ: view.thalamusRadiusZ ?? view.ThalamusRadiusZ ?? 0.38,
        nucleiCount: view.nucleiCount ?? view.NucleiCount ?? 6,
        nucleiRadius: view.nucleiRadius ?? view.NucleiRadius ?? 0.10,

        enableColumnBanding: view.enableColumnBanding ?? view.EnableColumnBanding ?? true,
        columnBandPeriod: view.columnBandPeriod ?? view.ColumnBandPeriod ?? 2,
        columnBandStrength: view.columnBandStrength ?? view.ColumnBandStrength ?? 0.18
    };
}

function computeBounds(nodes) {
    let minX = Infinity, minY = Infinity, minZ = Infinity;
    let maxX = -Infinity, maxY = -Infinity, maxZ = -Infinity;

    for (const n of nodes) {
        const p = n.pos ?? n.Pos;
        const x = (p.x ?? p.X);
        const y = (p.y ?? p.Y);
        const z = (p.z ?? p.Z);

        if (x < minX) minX = x; if (x > maxX) maxX = x;
        if (y < minY) minY = y; if (y > maxY) maxY = y;
        if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
    }

    if (!isFinite(minX)) {
        minX = minY = minZ = 0;
        maxX = maxY = maxZ = 0;
    }

    return { minX, minY, minZ, maxX, maxY, maxZ };
}

function buildNucleiCenters(bounds, count) {
    const cx = (bounds.minX + bounds.maxX) * 0.5;
    const cy = (bounds.minY + bounds.maxY) * 0.5;
    const spanX = Math.max(1, (bounds.maxX - bounds.minX));
    const spanY = Math.max(1, (bounds.maxY - bounds.minY));
    const span = Math.max(spanX, spanY);

    const cz = bounds.minZ + (bounds.maxZ - bounds.minZ) * 0.35;
    const ringR = span * 0.18;

    const centers = [];
    const c = Math.max(0, Math.min(24, count | 0));
    if (c <= 0) return centers;

    for (let i = 0; i < c; i++) {
        const t = (i / c) * Math.PI * 2.0;
        const r = ringR * (0.80 + 0.20 * (i % 2));
        centers.push({
            x: cx + Math.cos(t) * r,
            y: cy + Math.sin(t) * r,
            z: cz + ((i % 3) - 1) * 0.35
        });
    }

    return centers;
}

function regionWeights(p, bounds, view, nucleiCenters) {
    const cx = (bounds.minX + bounds.maxX) * 0.5;
    const cy = (bounds.minY + bounds.maxY) * 0.5;

    const spanX = Math.max(1, (bounds.maxX - bounds.minX));
    const spanY = Math.max(1, (bounds.maxY - bounds.minY));
    const spanZ = Math.max(1, (bounds.maxZ - bounds.minZ));

    const rx = spanX * clamp(view.thalamusRadiusXY, 0.05, 0.95);
    const ry = spanY * clamp(view.thalamusRadiusXY, 0.05, 0.95);
    const rz = spanZ * clamp(view.thalamusRadiusZ, 0.05, 0.95);

    const cz = bounds.minZ + spanZ * 0.35;

    const dx = (p.x0 - cx) / rx;
    const dy = (p.y0 - cy) / ry;
    const dz = (p.z0 - cz) / rz;

    const ell = (dx * dx) + (dy * dy) + (dz * dz);

    let th = clamp(1.0 - ell, 0.0, 1.0);
    th = th * th * th;

    let nu = 0.0;
    const nr = Math.max(0.0001, (Math.max(spanX, spanY) * clamp(view.nucleiRadius, 0.03, 0.80)));
    const invNr2 = 1.0 / (nr * nr);

    for (let i = 0; i < nucleiCenters.length; i++) {
        const c = nucleiCenters[i];
        const ax = p.x0 - c.x;
        const ay = p.y0 - c.y;
        const az = p.z0 - c.z;
        const d2 = (ax * ax) + (ay * ay) + (az * az);
        const w = Math.exp(-d2 * invNr2);
        if (w > nu) nu = w;
    }

    const cortex = clamp(1.0 - (th * 0.80) - (nu * 0.90), 0.10, 1.0);
    return { cortex, thalamus: th, nuclei: nu };
}

function columnBand(p, view) {
    if (!view.enableColumnBanding) return 0.0;
    const period = Math.max(1, view.columnBandPeriod | 0);
    const bx = (p.x0 % period) === 0 ? 1.0 : -1.0;
    const by = (p.y0 % period) === 0 ? 1.0 : -1.0;
    return ((bx + by) * 0.5) * clamp(view.columnBandStrength, 0.0, 1.0);
}

function baseRegionColor(weights) {
    const cortex = { r: 0.18, g: 0.28, b: 0.62 };
    const thal = { r: 0.75, g: 0.22, b: 0.88 };
    const nuc = { r: 0.92, g: 0.72, b: 0.20 };

    const tNu = clamp(weights.nuclei, 0.0, 1.0);
    const tTh = clamp(weights.thalamus, 0.0, 1.0);
    const tCx = clamp(weights.cortex, 0.0, 1.0);

    let r = cortex.r * tCx + thal.r * tTh + nuc.r * tNu;
    let g = cortex.g * tCx + thal.g * tTh + nuc.g * tNu;
    let b = cortex.b * tCx + thal.b * tTh + nuc.b * tNu;

    const s = Math.max(0.25, (tCx + tTh + tNu));
    r /= s; g /= s; b /= s;

    return { r: clamp(r, 0, 1), g: clamp(g, 0, 1), b: clamp(b, 0, 1) };
}

function hash01(a, b) {
    // cheap deterministic hash -> [0..1)
    let x = (a * 73856093) ^ (b * 19349663);
    x = (x ^ (x >>> 13)) * 1274126177;
    x = (x ^ (x >>> 16)) >>> 0;
    return (x / 4294967296.0);
}

export function createNetwork3D(hostEl) {
    const THREE = requireThree();

    const scene = new THREE.Scene();

    const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
    renderer.setPixelRatio(window.devicePixelRatio || 1);
    renderer.setSize(hostEl.clientWidth, hostEl.clientHeight);
    hostEl.appendChild(renderer.domElement);

    const camera = new THREE.PerspectiveCamera(60, hostEl.clientWidth / hostEl.clientHeight, 0.1, 6000);
    camera.position.set(10, 12, 18);

    const controls = new THREE.OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.dampingFactor = 0.08;

    const light1 = new THREE.DirectionalLight(0xffffff, 0.85);
    light1.position.set(20, 30, 10);
    scene.add(light1);

    const light2 = new THREE.DirectionalLight(0xffffff, 0.35);
    light2.position.set(-18, 10, -12);
    scene.add(light2);

    const amb = new THREE.AmbientLight(0xffffff, 0.25);
    scene.add(amb);

    const overlays = { grid: null, axes: null, laminaGroup: null };

    const raycaster = new THREE.Raycaster();
    const pointer = new THREE.Vector2(9999, 9999);

    const state = {
        scene, renderer, camera, controls, hostEl,

        nodeMesh: null,    // InstancedMesh
        nodeGeom: null,    // SphereGeometry
        nodeMat: null,     // MeshStandardMaterial

        edgesObj: null,
        edgeGeom: null,

        overlays,
        _raf: 0,
        _onResize: null,
        _onPointerMove: null,
        _onPointerLeave: null,

        _lastStepIndex: -1,
        _lastNodeCount: 0,
        _lastRadius: 0,

        _pointer: pointer,
        _raycaster: raycaster,
        _hoverId: null,
        _hoverPos: null,

        _posById: null,
        _idsByInstance: null,
        _view: null
    };

    function animate() {
        if (state.nodeMesh && state._view) {
            pickHover(state);
        }

        state.controls.update();
        state.renderer.render(state.scene, state.camera);
        state._raf = requestAnimationFrame(animate);
    }
    state._raf = requestAnimationFrame(animate);

    state._onResize = () => {
        const w = state.hostEl.clientWidth;
        const h = state.hostEl.clientHeight;
        state.renderer.setSize(w, h);
        state.camera.aspect = w / h;
        state.camera.updateProjectionMatrix();
    };
    window.addEventListener("resize", state._onResize);

    state._onPointerMove = (ev) => {
        const rect = state.renderer.domElement.getBoundingClientRect();
        const x = (ev.clientX - rect.left) / rect.width;
        const y = (ev.clientY - rect.top) / rect.height;
        state._pointer.x = (x * 2) - 1;
        state._pointer.y = -((y * 2) - 1);
    };
    state._onPointerLeave = () => {
        state._pointer.x = 9999;
        state._pointer.y = 9999;
        state._hoverId = null;
        state._hoverPos = null;
    };

    state.renderer.domElement.addEventListener("pointermove", state._onPointerMove);
    state.renderer.domElement.addEventListener("pointerleave", state._onPointerLeave);

    return state;
}

export function updateNetwork3D(state, snapshotRaw, viewRaw) {
    if (!state || !snapshotRaw) return;

    const THREE = requireThree();
    const snap = normalizeSnapshot(snapshotRaw);
    const view = normalizeView(viewRaw);

    state._view = view;

    if (view.enableFog) {
        state.scene.fog = new THREE.FogExp2(0x000000, clamp(view.fogDensity, 0.0, 0.2));
    } else {
        state.scene.fog = null;
    }

    updateOverlays(state, THREE, snap, view);

    const nodes = snap.nodes;
    const synapses = snap.synapses;

    const bounds = computeBounds(nodes);
    const nucleiCenters = buildNucleiCenters(bounds, view.nucleiCount);

    const posById = new Map();
    const idsByInstance = new Array(nodes.length);

    for (let i = 0; i < nodes.length; i++) {
        const n = nodes[i];
        const id = n.id ?? n.Id;

        const pos = n.pos ?? n.Pos;
        const x0 = (pos.x ?? pos.X);
        const y0 = (pos.y ?? pos.Y);
        const z0 = (pos.z ?? pos.Z);

        const x = x0 * view.spacingXY;
        const y = y0 * view.spacingXY;
        const z = z0 * view.zScale;

        posById.set(id, { x, y, z, x0, y0, z0 });
        idsByInstance[i] = id;
    }

    state._posById = posById;
    state._idsByInstance = idsByInstance;

    if (state._hoverId != null && !posById.has(state._hoverId)) {
        state._hoverId = null;
        state._hoverPos = null;
    } else if (state._hoverId != null) {
        const hp = posById.get(state._hoverId);
        state._hoverPos = hp ? { x0: hp.x0, y0: hp.y0, z0: hp.z0 } : null;
    }

    const nodeCount = nodes.length;
    const needRebuildNodes =
        (!state.nodeMesh || !state.nodeGeom || !state.nodeMat || state._lastNodeCount !== nodeCount);

    if (needRebuildNodes) {
        if (state.nodeMesh) {
            state.scene.remove(state.nodeMesh);
            state.nodeMesh.geometry.dispose();
            state.nodeMesh.material.dispose();
            state.nodeMesh = null;
            state.nodeGeom = null;
            state.nodeMat = null;
        }

        const geom = new THREE.SphereGeometry(1, 12, 10);

        const mat = new THREE.MeshStandardMaterial({
            vertexColors: true,
            transparent: true,
            opacity: clamp(view.nodeOpacity, 0.1, 1.0),
            roughness: 0.55,
            metalness: 0.05
        });

        const mesh = new THREE.InstancedMesh(geom, mat, nodeCount);
        mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);

        // Enable per-instance colours
        mesh.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(nodeCount * 3), 3);

        state.nodeGeom = geom;
        state.nodeMat = mat;
        state.nodeMesh = mesh;
        state.scene.add(mesh);
    } else {
        state.nodeMat.opacity = clamp(view.nodeOpacity, 0.1, 1.0);
    }

    // Update instances (positions, colours, sizes)
    {
        const hover = state._hoverPos;
        const mesh = state.nodeMesh;

        const tmpMat = new THREE.Matrix4();
        const tmpPos = new THREE.Vector3();
        const tmpScale = new THREE.Vector3();
        const tmpQuat = new THREE.Quaternion();

        const baseR = Math.max(0.01, view.nodeSize);

        for (let i = 0; i < nodeCount; i++) {
            const n = nodes[i];
            const id = idsByInstance[i];
            const p = posById.get(id);
            if (!p) continue;

            // Base region colour
            let rgb = { r: 0.35, g: 0.35, b: 0.35 };

            if (view.enableRegions) {
                const w = regionWeights(p, bounds, view, nucleiCenters);
                rgb = baseRegionColor(w);
            }

            // Activity overlay
            const vRaw = n.v ?? n.V ?? 0;
            const spiked = n.spiked ?? n.Spiked ?? false;
            const v = clamp((vRaw + 5.0) / 10.0, 0.0, 1.0);

            rgb.r = clamp(rgb.r + (v * 0.20), 0, 1);
            rgb.b = clamp(rgb.b + ((1.0 - v) * 0.10), 0, 1);

            if (spiked) {
                rgb.r = clamp(rgb.r + 0.20, 0, 1);
                rgb.g = clamp(rgb.g + 0.20, 0, 1);
                rgb.b = clamp(rgb.b + 0.20, 0, 1);
            }

            // Minicolumn banding
            const band = columnBand(p, view);
            if (band !== 0) {
                const t = clamp(band, -0.5, 0.5);
                rgb.r = clamp(rgb.r + t * 0.08, 0, 1);
                rgb.g = clamp(rgb.g + t * 0.08, 0, 1);
                rgb.b = clamp(rgb.b + t * 0.10, 0, 1);
            }

            // Hover boosts
            let rScale = baseR;
            if (hover) {
                if (p.x0 === hover.x0 && p.y0 === hover.y0) {
                    const boost = clamp(view.columnBoost, 0.0, 2.0) * 0.12;
                    rgb.r = clamp(rgb.r + boost, 0, 1);
                    rgb.g = clamp(rgb.g + boost, 0, 1);
                    rgb.b = clamp(rgb.b + boost, 0, 1);
                }
                if (id === state._hoverId) {
                    const boost = clamp(view.hoverBoost, 0.0, 2.0) * 0.18;
                    rgb.r = clamp(rgb.r + boost, 0, 1);
                    rgb.g = clamp(rgb.g + boost, 0, 1);
                    rgb.b = clamp(rgb.b + boost, 0, 1);
                    rScale = baseR * 1.55;
                }
            }

            // Instance transform
            tmpPos.set(p.x, p.y, p.z);
            tmpQuat.set(0, 0, 0, 1);
            tmpScale.set(rScale, rScale, rScale);
            tmpMat.compose(tmpPos, tmpQuat, tmpScale);
            mesh.setMatrixAt(i, tmpMat);

            mesh.setColorAt(i, new THREE.Color(rgb.r, rgb.g, rgb.b));
        }

        mesh.instanceMatrix.needsUpdate = true;
        if (mesh.instanceColor) mesh.instanceColor.needsUpdate = true;
    }

    // --- Edges: rebuild with aggressive thinning for readability ---
    if (state.edgesObj) {
        state.scene.remove(state.edgesObj);
        state.edgeGeom.dispose();
        state.edgesObj.material.dispose();
        state.edgesObj = null;
        state.edgeGeom = null;
    }

    const edgePositions = [];
    const edgeColors = [];

    const softFade = Math.max(0.0001, view.edgeSoftFadeLen);
    const hardCull = Math.max(softFade + 0.0001, view.edgeHardCullLen);

    for (let i = 0; i < synapses.length; i++) {
        const e = synapses[i];

        const pre = e.pre ?? e.Pre;
        const post = e.post ?? e.Post;
        const wRaw = e.w ?? e.W ?? 0;
        const kind = e.kind ?? e.Kind ?? 0;

        const a = posById.get(pre);
        const b = posById.get(post);
        if (!a || !b) continue;

        const dx = b.x - a.x;
        const dy = b.y - a.y;
        const dz = b.z - a.z;
        const len = Math.sqrt(dx * dx + dy * dy + dz * dz);

        if (len > hardCull) continue;

        // Thin long-ish edges even if within cull radius:
        // Probability drops with length to reveal local structure.
        const len01 = clamp(len / hardCull, 0.0, 1.0);
        const keepProb = clamp(1.0 - (len01 * len01 * 0.85), 0.12, 1.0);
        if (hash01(pre, post) > keepProb) continue;

        const w = Math.min(Math.abs(wRaw), 2.0);
        let bright = clamp(0.10 + (w / 2.0) * 0.90, 0.10, 1.0);

        if (len > softFade) {
            const t = clamp((len - softFade) / (hardCull - softFade), 0.0, 1.0);
            bright *= (1.0 - 0.80 * (t * t));
        }

        let r = kind === 0 ? 1.0 : 0.25;
        let g = kind === 0 ? 0.62 : 0.45;
        let bb = kind === 0 ? 0.22 : 1.0;

        // Slight desaturation for far edges (adds depth)
        const desat = clamp(len01 * 0.35, 0.0, 0.35);
        r = lerp(r, 0.55, desat);
        g = lerp(g, 0.55, desat);
        bb = lerp(bb, 0.55, desat);

        r *= bright; g *= bright; bb *= bright;

        edgePositions.push(a.x, a.y, a.z, b.x, b.y, b.z);
        edgeColors.push(r, g, bb, r, g, bb);
    }

    const epos = new Float32Array(edgePositions);
    const ecol = new Float32Array(edgeColors);

    const edgeGeom = new THREE.BufferGeometry();
    edgeGeom.setAttribute("position", new THREE.BufferAttribute(epos, 3));
    edgeGeom.setAttribute("color", new THREE.BufferAttribute(ecol, 3));

    const edgeMat = new THREE.LineBasicMaterial({
        vertexColors: true,
        transparent: true,
        opacity: clamp(view.edgeOpacity, 0.05, 1.0),
        depthWrite: false
    });

    const edgeLines = new THREE.LineSegments(edgeGeom, edgeMat);

    state.edgeGeom = edgeGeom;
    state.edgesObj = edgeLines;
    state.scene.add(edgeLines);

    // Auto-fit camera when topology changes noticeably
    if (nodeCount > 0) {
        const bbx = new THREE.Box3().setFromObject(state.nodeMesh);
        const size = bbx.getSize(new THREE.Vector3());
        const center = bbx.getCenter(new THREE.Vector3());

        const rawRadius = Math.max(size.x, size.y, size.z);
        const radius = Math.max(2.0, rawRadius * 0.75 + 1.0);

        const stepIndex = snap.stepIndex;
        const stepDecreased = (state._lastStepIndex >= 0 && stepIndex < state._lastStepIndex);
        const likelyNewRun = (stepIndex === 0 && state._lastStepIndex > 0);
        const nodeCountChanged = (nodeCount !== state._lastNodeCount);

        const lastR = Math.max(0.0001, state._lastRadius);
        const radiusDeltaFrac = Math.abs(radius - lastR) / lastR;
        const radiusChangedALot = radiusDeltaFrac > 0.35;

        const shouldFit = stepDecreased || likelyNewRun || nodeCountChanged || radiusChangedALot;

        state._lastStepIndex = stepIndex;
        state._lastNodeCount = nodeCount;
        state._lastRadius = radius;

        if (shouldFit) {
            state.controls.target.copy(center);

            let dir = new THREE.Vector3().subVectors(state.camera.position, center);
            if (dir.lengthSq() < 0.001) dir.set(0.5, 0.6, 0.8);
            dir.normalize();

            state.camera.position.copy(center.clone().add(dir.multiplyScalar(radius * 2.0)));
            state.camera.updateProjectionMatrix();
        }
    }
}

function updateOverlays(state, THREE, snap, view) {
    if (view.showGrid && !state.overlays.grid) {
        const g = new THREE.GridHelper(200, 50, 0x444444, 0x222222);
        g.position.set(0, 0, 0);
        g.material.transparent = true;
        g.material.opacity = 0.18;
        state.overlays.grid = g;
        state.scene.add(g);
    }
    if (!view.showGrid && state.overlays.grid) {
        state.scene.remove(state.overlays.grid);
        state.overlays.grid = null;
    }

    if (view.showAxes && !state.overlays.axes) {
        const a = new THREE.AxesHelper(10);
        a.material.transparent = true;
        a.material.opacity = 0.55;
        state.overlays.axes = a;
        state.scene.add(a);
    }
    if (!view.showAxes && state.overlays.axes) {
        state.scene.remove(state.overlays.axes);
        state.overlays.axes = null;
    }

    if (!view.showLaminarPlanes) {
        if (state.overlays.laminaGroup) {
            state.scene.remove(state.overlays.laminaGroup);
            disposeGroup(state.overlays.laminaGroup);
            state.overlays.laminaGroup = null;
        }
        return;
    }

    const nodes = snap.nodes;
    if (!nodes || nodes.length === 0) return;

    const b = computeBounds(nodes);

    if (state.overlays.laminaGroup) {
        state.scene.remove(state.overlays.laminaGroup);
        disposeGroup(state.overlays.laminaGroup);
        state.overlays.laminaGroup = null;
    }

    const grp = new THREE.Group();
    const width = 220;
    const height = 220;

    for (let z0 = b.minZ; z0 <= b.maxZ; z0++) {
        const z = z0 * view.zScale;

        const mat = new THREE.MeshBasicMaterial({
            color: new THREE.Color(0.10, 0.10, 0.12),
            transparent: true,
            opacity: 0.04,
            depthWrite: false
        });

        const geom = new THREE.PlaneGeometry(width, height);
        const plane = new THREE.Mesh(geom, mat);
        plane.rotation.x = Math.PI / 2;
        plane.position.set(0, 0, z);

        grp.add(plane);
    }

    state.overlays.laminaGroup = grp;
    state.scene.add(grp);
}

function disposeGroup(group) {
    if (!group) return;
    group.traverse(obj => {
        if (obj.geometry) obj.geometry.dispose();
        if (obj.material) obj.material.dispose();
    });
}

export function disposeNetwork3D(state) {
    if (!state) return;

    cancelAnimationFrame(state._raf);
    window.removeEventListener("resize", state._onResize);

    if (state.renderer && state.renderer.domElement) {
        state.renderer.domElement.removeEventListener("pointermove", state._onPointerMove);
        state.renderer.domElement.removeEventListener("pointerleave", state._onPointerLeave);
    }

    if (state.overlays.grid) state.scene.remove(state.overlays.grid);
    if (state.overlays.axes) state.scene.remove(state.overlays.axes);
    if (state.overlays.laminaGroup) {
        state.scene.remove(state.overlays.laminaGroup);
        disposeGroup(state.overlays.laminaGroup);
    }

    if (state.nodeMesh) {
        state.scene.remove(state.nodeMesh);
        state.nodeMesh.geometry.dispose();
        state.nodeMesh.material.dispose();
        state.nodeMesh = null;
    }

    if (state.edgesObj) {
        state.scene.remove(state.edgesObj);
        state.edgeGeom.dispose();
        state.edgesObj.material.dispose();
        state.edgesObj = null;
        state.edgeGeom = null;
    }

    state.renderer.dispose();

    if (state.renderer.domElement && state.renderer.domElement.parentElement) {
        state.renderer.domElement.parentElement.removeChild(state.renderer.domElement);
    }
}

// ------------------------- hover picking (InstancedMesh) -------------------------

function pickHover(state) {
    if (state._pointer.x > 10 || state._pointer.y > 10) {
        if (state._hoverId != null) {
            state._hoverId = null;
            state._hoverPos = null;
        }
        return;
    }

    state._raycaster.setFromCamera(state._pointer, state.camera);

    const intersects = state._raycaster.intersectObject(state.nodeMesh, false);
    if (!intersects || intersects.length === 0) {
        if (state._hoverId != null) {
            state._hoverId = null;
            state._hoverPos = null;
        }
        return;
    }

    const hit = intersects[0];
    const inst = hit.instanceId;

    if (inst == null || !state._idsByInstance) return;

    const id = state._idsByInstance[inst];
    state._hoverId = id;

    const p = state._posById ? state._posById.get(id) : null;
    state._hoverPos = p ? { x0: p.x0, y0: p.y0, z0: p.z0 } : null;
}
