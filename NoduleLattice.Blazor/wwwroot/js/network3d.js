// ============================================================================
// FILE: NoduleLattice.Blazor/wwwroot/js/network3d.js
// PURPOSE:
//   Cortex-look visual pass:
//     - XY spacing + Z scaling to make laminar slab obvious
//     - Optional laminar planes and grid/axes overlays
//     - Edge thinning: fade + hard cull by length
//     - Reuse geometries when counts match (faster for real-time)
//     - Hover: highlight cortical column (same X/Y across layers)
// ============================================================================

function requireThree() {
    if (!window.THREE) throw new Error("THREE is not loaded. Ensure App.razor includes three.min.js.");
    if (!window.THREE.OrbitControls) throw new Error("OrbitControls is not loaded. Ensure App.razor includes OrbitControls.js.");
    return window.THREE;
}

function clamp(x, lo, hi) {
    return x < lo ? lo : (x > hi ? hi : x);
}

function layerTint01(zNorm) {
    const cool = clamp(1.0 - zNorm, 0.0, 1.0);
    const warm = clamp(zNorm, 0.0, 1.0);
    return {
        r: 0.10 * warm,
        g: 0.08 * (0.35 + 0.65 * zNorm),
        b: 0.12 * cool
    };
}

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
        spacingXY: view.spacingXY ?? view.SpacingXY ?? 1.35,
        zScale: view.zScale ?? view.ZScale ?? 1.80,

        nodeSize: view.nodeSize ?? view.NodeSize ?? 0.26,
        nodeOpacity: view.nodeOpacity ?? view.NodeOpacity ?? 0.95,

        edgeSoftFadeLen: view.edgeSoftFadeLen ?? view.EdgeSoftFadeLen ?? 5.0,
        edgeHardCullLen: view.edgeHardCullLen ?? view.EdgeHardCullLen ?? 8.0,
        edgeOpacity: view.edgeOpacity ?? view.EdgeOpacity ?? 0.55,

        showGrid: view.showGrid ?? view.ShowGrid ?? true,
        showAxes: view.showAxes ?? view.ShowAxes ?? false,
        showLaminarPlanes: view.showLaminarPlanes ?? view.ShowLaminarPlanes ?? true,

        enableFog: view.enableFog ?? view.EnableFog ?? true,
        fogDensity: view.fogDensity ?? view.FogDensity ?? 0.018,

        hoverPointThreshold: view.hoverPointThreshold ?? view.HoverPointThreshold ?? 0.55,
        columnBoost: view.columnBoost ?? view.ColumnBoost ?? 0.55,
        hoverBoost: view.hoverBoost ?? view.HoverBoost ?? 0.90
    };
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

    // Overlays
    const overlays = {
        grid: null,
        axes: null,
        laminaGroup: null
    };

    // Hover picking
    const raycaster = new THREE.Raycaster();
    const pointer = new THREE.Vector2(9999, 9999);

    const state = {
        scene,
        renderer,
        camera,
        controls,
        hostEl,

        nodesObj: null,
        edgesObj: null,

        nodeGeom: null,
        edgeGeom: null,

        overlays,
        _raf: 0,
        _onResize: null,
        _onPointerMove: null,
        _onPointerLeave: null,

        // Last snapshot characteristics (for re-use decisions)
        _lastStepIndex: -1,
        _lastNodeCount: 0,
        _lastEdgeCount: 0,
        _lastRadius: 0,

        // hover state
        _pointer: pointer,
        _raycaster: raycaster,
        _hoverId: null,
        _hoverPos: null,

        // cached id->pos map for edges
        _posById: null,

        // last applied view (shallow)
        _view: null
    };

    function animate() {
        if (state.nodesObj && state._view) {
            state._raycaster.params.Points.threshold = state._view.hoverPointThreshold;
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

    // Fog (cortical depth cue)
    if (view.enableFog) {
        // exponential fog, density tunable
        state.scene.fog = new THREE.FogExp2(0x000000, clamp(view.fogDensity, 0.0, 0.2));
    } else {
        state.scene.fog = null;
    }

    // Overlays: grid, axes, laminar planes
    updateOverlays(state, THREE, snap, view);

    const nodes = snap.nodes;
    const synapses = snap.synapses;

    // id -> scaled position
    const posById = new Map();
    let minZ = Infinity, maxZ = -Infinity;

    for (const n of nodes) {
        const id = n.id ?? n.Id;
        const pos = n.pos ?? n.Pos;

        const x0 = (pos.x ?? pos.X);
        const y0 = (pos.y ?? pos.Y);
        const z0 = (pos.z ?? pos.Z);

        const x = x0 * view.spacingXY;
        const y = y0 * view.spacingXY;
        const z = z0 * view.zScale;

        posById.set(id, { x, y, z, x0, y0, z0 });

        if (z0 < minZ) minZ = z0;
        if (z0 > maxZ) maxZ = z0;
    }

    state._posById = posById;

    // keep hover if possible
    if (state._hoverId != null && !posById.has(state._hoverId)) {
        state._hoverId = null;
        state._hoverPos = null;
    } else if (state._hoverId != null) {
        const hp = posById.get(state._hoverId);
        state._hoverPos = hp ? { x0: hp.x0, y0: hp.y0, z0: hp.z0 } : null;
    }

    const zSpan = Math.max(1, (maxZ - minZ));

    // ---------------- Nodes: reuse geometry when nodeCount unchanged ----------------

    const nodeCount = nodes.length;
    const needRebuildNodes = (!state.nodeGeom || !state.nodesObj || state._lastNodeCount !== nodeCount);

    if (needRebuildNodes) {
        // remove old
        if (state.nodesObj) {
            state.scene.remove(state.nodesObj);
            state.nodeGeom.dispose();
            state.nodesObj.material.dispose();
            state.nodesObj = null;
            state.nodeGeom = null;
        }

        const positions = new Float32Array(nodeCount * 3);
        const colors = new Float32Array(nodeCount * 3);

        const geom = new THREE.BufferGeometry();
        geom.setAttribute("position", new THREE.BufferAttribute(positions, 3));
        geom.setAttribute("color", new THREE.BufferAttribute(colors, 3));

        const mat = new THREE.PointsMaterial({
            size: view.nodeSize,
            vertexColors: true,
            transparent: true,
            opacity: view.nodeOpacity,
            depthWrite: false
        });

        const points = new THREE.Points(geom, mat);

        state.nodeGeom = geom;
        state.nodesObj = points;
        state.scene.add(points);
    } else {
        // update node material live
        state.nodesObj.material.size = view.nodeSize;
        state.nodesObj.material.opacity = view.nodeOpacity;
    }

    // Update node buffers (positions + colors)
    {
        const posAttr = state.nodeGeom.getAttribute("position");
        const colAttr = state.nodeGeom.getAttribute("color");

        const hover = state._hoverPos; // {x0,y0,z0} in *unscaled* grid coords

        for (let i = 0; i < nodeCount; i++) {
            const n = nodes[i];

            const id = n.id ?? n.Id;
            const p = posById.get(id);
            if (!p) continue;

            posAttr.setXYZ(i, p.x, p.y, p.z);

            const vRaw = n.v ?? n.V ?? 0;
            const spiked = n.spiked ?? n.Spiked ?? false;

            const v = clamp((vRaw + 5.0) / 10.0, 0.0, 1.0);

            let r = v;
            let g = 0.25 + 0.35 * (1.0 - Math.abs(v - 0.5) * 2.0);
            let b = 1.0 - v;

            if (spiked) {
                r = clamp(r + 0.35, 0.0, 1.0);
                g = clamp(g + 0.35, 0.0, 1.0);
                b = clamp(b + 0.35, 0.0, 1.0);
            }

            // laminar tint by z-layer (unscaled z0)
            const zNorm = (p.z0 - minZ) / zSpan;
            const t = layerTint01(zNorm);
            r = clamp(r + t.r, 0.0, 1.0);
            g = clamp(g + t.g, 0.0, 1.0);
            b = clamp(b + t.b, 0.0, 1.0);

            // Column highlight (same x0/y0)
            if (hover) {
                if (p.x0 === hover.x0 && p.y0 === hover.y0) {
                    r = clamp(r + view.columnBoost * 0.35, 0.0, 1.0);
                    g = clamp(g + view.columnBoost * 0.35, 0.0, 1.0);
                    b = clamp(b + view.columnBoost * 0.35, 0.0, 1.0);
                }
                if (id === state._hoverId) {
                    r = clamp(r + view.hoverBoost * 0.40, 0.0, 1.0);
                    g = clamp(g + view.hoverBoost * 0.40, 0.0, 1.0);
                    b = clamp(b + view.hoverBoost * 0.40, 0.0, 1.0);
                }
            }

            colAttr.setXYZ(i, r, g, b);
        }

        posAttr.needsUpdate = true;
        colAttr.needsUpdate = true;
    }

    // ---------------- Edges: rebuild each time (count varies); still thinned ----------------

    // Remove old edges
    if (state.edgesObj) {
        state.scene.remove(state.edgesObj);
        state.edgeGeom.dispose();
        state.edgesObj.material.dispose();
        state.edgesObj = null;
        state.edgeGeom = null;
    }

    const edgePositions = [];
    const edgeColors = [];
    const hoverId = state._hoverId;

    const softFade = Math.max(0.0001, view.edgeSoftFadeLen);
    const hardCull = Math.max(softFade + 0.0001, view.edgeHardCullLen);

    for (let i = 0; i < synapses.length; i++) {
        const e = synapses[i];

        const pre = e.pre ?? e.Pre;
        const post = e.post ?? e.Post;
        const wRaw = e.w ?? e.W ?? 0;
        const kind = e.kind ?? e.Kind ?? 0;

        const a = posById.get(pre);
        const bpos = posById.get(post);
        if (!a || !bpos) continue;

        const dx = bpos.x - a.x;
        const dy = bpos.y - a.y;
        const dz = bpos.z - a.z;
        const len = Math.sqrt(dx * dx + dy * dy + dz * dz);

        if (len > hardCull) continue;

        // brightness from weight magnitude
        const w = Math.min(Math.abs(wRaw), 2.0);
        let bright = clamp(0.12 + (w / 2.0) * 0.88, 0.12, 1.0);

        // fade longer edges
        if (len > softFade) {
            const t = clamp((len - softFade) / (hardCull - softFade), 0.0, 1.0);
            bright *= (1.0 - 0.75 * (t * t));
        }

        // kind coloring (exc vs inh)
        let r = kind === 0 ? 1.0 : 0.25;
        let g = kind === 0 ? 0.65 : 0.45;
        let bb = kind === 0 ? 0.25 : 1.0;

        // hover boost for incident edges
        if (hoverId != null && (pre === hoverId || post === hoverId)) {
            bright = clamp(bright * 1.25, 0.0, 1.0);
        }

        r *= bright; g *= bright; bb *= bright;

        edgePositions.push(a.x, a.y, a.z, bpos.x, bpos.y, bpos.z);
        edgeColors.push(r, g, bb, r, g, bb);
    }

    const epos = new Float32Array(edgePositions);
    const ecolor = new Float32Array(edgeColors);

    const edgeGeom = new THREE.BufferGeometry();
    edgeGeom.setAttribute("position", new THREE.BufferAttribute(epos, 3));
    edgeGeom.setAttribute("color", new THREE.BufferAttribute(ecolor, 3));

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

    // ---------------- Auto-fit camera if lattice size changed ----------------

    if (nodeCount > 0) {
        const bbx = new THREE.Box3().setFromObject(state.nodesObj);
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
    // Grid
    if (view.showGrid && !state.overlays.grid) {
        const g = new THREE.GridHelper(200, 50, 0x444444, 0x222222);
        g.position.set(0, 0, 0);
        g.material.transparent = true;
        g.material.opacity = 0.25;
        state.overlays.grid = g;
        state.scene.add(g);
    }
    if (!view.showGrid && state.overlays.grid) {
        state.scene.remove(state.overlays.grid);
        state.overlays.grid = null;
    }

    // Axes
    if (view.showAxes && !state.overlays.axes) {
        const a = new THREE.AxesHelper(10);
        a.material.transparent = true;
        a.material.opacity = 0.6;
        state.overlays.axes = a;
        state.scene.add(a);
    }
    if (!view.showAxes && state.overlays.axes) {
        state.scene.remove(state.overlays.axes);
        state.overlays.axes = null;
    }

    // Laminar planes (one per integer Z layer)
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

    // determine z range in *unscaled* coordinates
    let minZ = Infinity, maxZ = -Infinity;
    for (const n of nodes) {
        const p = n.pos ?? n.Pos;
        const z0 = (p.z ?? p.Z);
        if (z0 < minZ) minZ = z0;
        if (z0 > maxZ) maxZ = z0;
    }

    // rebuild group each update (cheap: only a few planes)
    if (state.overlays.laminaGroup) {
        state.scene.remove(state.overlays.laminaGroup);
        disposeGroup(state.overlays.laminaGroup);
        state.overlays.laminaGroup = null;
    }

    const grp = new THREE.Group();

    const width = 220;
    const height = 220;

    for (let z0 = minZ; z0 <= maxZ; z0++) {
        const z = z0 * view.zScale;

        const zNorm = (z0 - minZ) / Math.max(1, (maxZ - minZ));
        const tint = layerTint01(zNorm);

        const mat = new THREE.MeshBasicMaterial({
            color: new THREE.Color(0.08 + tint.r, 0.08 + tint.g, 0.08 + tint.b),
            transparent: true,
            opacity: 0.06,
            depthWrite: false
        });

        const geom = new THREE.PlaneGeometry(width, height);
        const plane = new THREE.Mesh(geom, mat);
        plane.rotation.x = Math.PI / 2; // XY plane
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

    if (state.nodesObj) {
        state.scene.remove(state.nodesObj);
        state.nodeGeom.dispose();
        state.nodesObj.material.dispose();
    }

    if (state.edgesObj) {
        state.scene.remove(state.edgesObj);
        state.edgeGeom.dispose();
        state.edgesObj.material.dispose();
    }

    state.renderer.dispose();

    if (state.renderer.domElement && state.renderer.domElement.parentElement) {
        state.renderer.domElement.parentElement.removeChild(state.renderer.domElement);
    }
}

// ------------------------- hover picking -------------------------

function pickHover(state) {
    // pointer offscreen clears hover
    if (state._pointer.x > 10 || state._pointer.y > 10) {
        if (state._hoverId != null) {
            state._hoverId = null;
            state._hoverPos = null;
        }
        return;
    }

    state._raycaster.setFromCamera(state._pointer, state.camera);

    const intersects = state._raycaster.intersectObject(state.nodesObj, false);
    if (!intersects || intersects.length === 0) {
        if (state._hoverId != null) {
            state._hoverId = null;
            state._hoverPos = null;
        }
        return;
    }

    const hit = intersects[0];
    const idx = hit.index;

    const posAttr = state.nodeGeom.getAttribute("position");
    const hx = posAttr.getX(idx);
    const hy = posAttr.getY(idx);
    const hz = posAttr.getZ(idx);

    // convert scaled position -> unscaled integer grid (best-effort)
    const vx = state._view ? state._view.spacingXY : 1.0;
    const vz = state._view ? state._view.zScale : 1.0;

    const gx = Math.round(hx / vx);
    const gy = Math.round(hy / vx);
    const gz = Math.round(hz / vz);

    state._hoverPos = { x0: gx, y0: gy, z0: gz };

    // ID inference: canon IDs are sequential and snapshot nodes order matches that.
    // If in future you break that assumption, we’ll embed ids as an attribute.
    state._hoverId = idx + 1;
}
